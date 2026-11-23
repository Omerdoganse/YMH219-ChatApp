using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ChatApp.Core.Models;
using ChatApp.Core.Protocol;

namespace ChatApp.Core.Network;

/// <summary>
/// TcpListener ile istemcileri kabul eden, mesajlari dagitan cok istemcili sohbet sunucusu.
/// Her istemci ayri bir asenkron gorevde (Task) dinlenir.
/// Olaylar arka plan is parcaciklarindan tetiklenir; arayuz tarafinda Invoke ile isaretlenmelidir.
/// </summary>
public sealed class ChatServerService : IDisposable
{
    /// <summary>Sunucuda tutulan tek bir istemci oturumu.</summary>
    private sealed class ClientSession(ConnectedClientInfo info, PacketConnection connection)
    {
        public ConnectedClientInfo Info { get; } = info;
        public PacketConnection Connection { get; } = connection;
        public string? CloseReason { get; set; }
    }

    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<int, ClientSession> _sessions = new();
    private readonly object _nameLock = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>Sunucu calisiyor mu?</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Dinlenen port.</summary>
    public int Port { get; private set; }

    /// <summary>Su an bagli istemcilerin anlik listesi.</summary>
    public IReadOnlyList<ConnectedClientInfo> Clients =>
        _sessions.Values.Select(s => s.Info).OrderBy(i => i.Id).ToList();

    /// <summary>Yeni bir istemci kullanici adiyla katildiginda.</summary>
    public event EventHandler<ConnectedClientInfo>? ClientConnected;

    /// <summary>Bir istemci ayrildiginda (Reason: ayrilma nedeni).</summary>
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    /// <summary>Sunucudan gecen her sohbet/sistem mesaji (sunucu gunlugu icin).</summary>
    public event EventHandler<ChatPacket>? MessageRelayed;

    /// <summary>Teknik bilgi/uyari mesajlari.</summary>
    public event EventHandler<string>? Log;

    /// <summary>
    /// Sunucuyu verilen portta baslatir. Port kullanimdaysa ChatException firlatir.
    /// </summary>
    public void Start(int port)
    {
        if (IsRunning)
            throw new ChatException("Sunucu zaten calisiyor.");
        if (!ChatRules.IsValidPort(port))
            throw new ChatException("Port 1024 ile 65535 arasinda olmalidir.");

        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
        }
        catch (SocketException ex)
        {
            _listener = null;
            throw new ChatException($"{port} portu acilamadi (baska bir uygulama kullaniyor olabilir).\n{ex.Message}", ex);
        }

        Port = port;
        _cts = new CancellationTokenSource();
        IsRunning = true;
        OnLog($"Sunucu {port} portunda dinlemeye basladi.");
        _ = AcceptLoopAsync(_listener, _cts.Token);
    }

    /// <summary>
    /// Sunucuyu durdurur; bagli istemcilere bildirim gonderip baglantilarini kapatir.
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch (SocketException) { /* zaten kapali */ }
        _listener = null;

        foreach (ClientSession session in _sessions.Values.ToList())
        {
            session.CloseReason = "sunucu durduruldu";
            await SafeSendAsync(session, ChatPacket.SystemNotice("Sunucu kapatildi.")).ConfigureAwait(false);
            session.Connection.Dispose();
        }

        _cts = null; // iptal edilmis token hala istemci gorevlerinde kullanilabilir, bu yuzden Dispose edilmez
        OnLog("Sunucu durduruldu.");
    }

    /// <summary>
    /// Tum istemcilere sunucu duyurusu gonderir.
    /// </summary>
    public async Task SendAnnouncementAsync(string text)
    {
        if (!IsRunning)
            throw new ChatException("Duyuru gondermek icin sunucu calisiyor olmalidir.");
        if (ChatRules.ValidateMessage(text) is { } error)
            throw new ChatException(error);

        ChatPacket packet = ChatPacket.SystemNotice("Duyuru: " + text.Trim());
        MessageRelayed?.Invoke(this, packet);
        await BroadcastAsync(packet).ConfigureAwait(false);
    }

    /// <summary>
    /// Secilen istemcinin baglantisini sunucu tarafindan keser.
    /// </summary>
    public async Task KickAsync(int clientId)
    {
        if (!_sessions.TryGetValue(clientId, out ClientSession? session))
            throw new ChatException("Secilen istemci artik bagli degil.");

        session.CloseReason = "sunucu tarafindan baglantisi kesildi";
        await SafeSendAsync(session, ChatPacket.SystemNotice("Baglantiniz sunucu tarafindan kesildi.")).ConfigureAwait(false);
        session.Connection.Dispose(); // dinleme dongusu sonlanir ve temizlik yapilir
    }

    // ---------------- Ic isleyis ----------------

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex)
            {
                if (token.IsCancellationRequested) break;
                OnLog("Baglanti kabul edilemedi: " + ex.Message);
                continue;
            }

            _ = HandleClientAsync(tcp, token);
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken token)
    {
        PacketConnection connection;
        try
        {
            connection = new PacketConnection(tcp);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
        {
            tcp.Dispose();
            return;
        }

        ClientSession? session = null;
        string reason = "baglantiyi kapatti";

        try
        {
            session = await AcceptJoinAsync(connection, token).ConfigureAwait(false);
            if (session is null)
                return;

            ConnectedClientInfo info = session.Info;
            OnLog($"{info.UserName} baglandi ({info.RemoteEndPoint}).");
            ClientConnected?.Invoke(this, info);

            ChatPacket joined = ChatPacket.SystemNotice($"{info.UserName} sohbete katildi.");
            MessageRelayed?.Invoke(this, joined);
            await BroadcastAsync(joined).ConfigureAwait(false);
            await BroadcastUserListAsync().ConfigureAwait(false);

            // Istemciden gelen paketleri dinleme dongusu
            while (!token.IsCancellationRequested)
            {
                ChatPacket? packet = await connection.ReceiveAsync(token).ConfigureAwait(false);
                if (packet is null)
                    break;
                if (packet.Type == PacketType.Leave)
                {
                    reason = "sohbetten ayrildi";
                    break;
                }

                await HandlePacketAsync(session, packet).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { reason = "sunucu durduruldu"; }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            reason = "baglantisi koptu";
        }
        finally
        {
            connection.Dispose();
            if (session is not null && _sessions.TryRemove(session.Info.Id, out _))
            {
                reason = session.CloseReason ?? reason;
                OnLog($"{session.Info.UserName} {reason}.");
                ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(session.Info, reason));

                if (IsRunning)
                {
                    ChatPacket left = ChatPacket.SystemNotice($"{session.Info.UserName} sohbetten ayrildi.");
                    MessageRelayed?.Invoke(this, left);
                    await BroadcastAsync(left).ConfigureAwait(false);
                    await BroadcastUserListAsync().ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Ilk paketin Join olmasini bekler, kullanici adini dogrular ve oturumu kaydeder.
    /// Kabul edilmezse null doner.
    /// </summary>
    private async Task<ClientSession?> AcceptJoinAsync(PacketConnection connection, CancellationToken token)
    {
        using var joinCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        joinCts.CancelAfter(JoinTimeout);

        ChatPacket? first;
        try
        {
            first = await connection.ReceiveAsync(joinCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            OnLog("Bir istemci sure icinde kullanici adi gondermedigi icin reddedildi.");
            return null;
        }

        if (first is null || first.Type != PacketType.Join)
            return null;

        string userName = first.Sender.Trim();
        string? error = ChatRules.ValidateUserName(userName);

        ClientSession? session = null;
        if (error is null)
        {
            lock (_nameLock)
            {
                bool taken = _sessions.Values.Any(s =>
                    s.Info.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase));
                if (taken)
                {
                    error = $"'{userName}' kullanici adi zaten kullaniliyor.";
                }
                else
                {
                    session = new ClientSession(new ConnectedClientInfo(userName, connection.RemoteEndPoint), connection);
                    _sessions[session.Info.Id] = session;
                }
            }
        }

        if (session is null)
        {
            OnLog($"Katilma reddedildi ({userName}): {error}");
            await connection.SendAsync(new ChatPacket { Type = PacketType.JoinRejected, Sender = "Sunucu", Text = error ?? "" },
                token).ConfigureAwait(false);
            return null;
        }

        await connection.SendAsync(new ChatPacket
        {
            Type = PacketType.JoinAccepted,
            Sender = "Sunucu",
            Text = $"Sohbete hos geldin, {userName}!"
        }, token).ConfigureAwait(false);
        return session;
    }

    /// <summary>
    /// Istemciden gelen bir sohbet paketini isler.
    /// Gonderen adi ve zaman damgasi sunucuda yeniden atanir (istemciye guvenilmez).
    /// </summary>
    private async Task HandlePacketAsync(ClientSession session, ChatPacket packet)
    {
        if (packet.Type is not (PacketType.Message or PacketType.PrivateMessage))
            return; // istemcinin gonderemeyecegi turler yok sayilir

        if (ChatRules.ValidateMessage(packet.Text) is { } error)
        {
            await SafeSendAsync(session, ChatPacket.SystemNotice("Mesaj reddedildi: " + error)).ConfigureAwait(false);
            return;
        }

        packet.Sender = session.Info.UserName;
        packet.Timestamp = DateTime.Now;
        packet.Users = null;
        session.Info.IncrementMessageCount();

        if (packet.Type == PacketType.Message)
        {
            packet.Target = null;
            MessageRelayed?.Invoke(this, packet);
            await BroadcastAsync(packet).ConfigureAwait(false);
            return;
        }

        await RoutePrivateMessageAsync(session, packet).ConfigureAwait(false);
    }

    /// <summary>
    /// Ozel mesaji yalnizca hedef kullaniciya ve gonderene iletir.
    /// </summary>
    private async Task RoutePrivateMessageAsync(ClientSession sender, ChatPacket packet)
    {
        ClientSession? target = _sessions.Values.FirstOrDefault(s =>
            s.Info.UserName.Equals(packet.Target, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            await SafeSendAsync(sender, ChatPacket.SystemNotice($"'{packet.Target}' adli kullanici cevrimici degil."))
                .ConfigureAwait(false);
            return;
        }

        packet.Target = target.Info.UserName;
        MessageRelayed?.Invoke(this, packet);
        await SafeSendAsync(target, packet).ConfigureAwait(false);
        if (target != sender)
            await SafeSendAsync(sender, packet).ConfigureAwait(false);
    }

    private Task BroadcastUserListAsync() =>
        BroadcastAsync(ChatPacket.UserListOf(Clients.Select(c => c.UserName)));

    private async Task BroadcastAsync(ChatPacket packet)
    {
        foreach (ClientSession session in _sessions.Values.ToList())
            await SafeSendAsync(session, packet).ConfigureAwait(false);
    }

    /// <summary>
    /// Gonderim hatasi tum sunucuyu etkilemesin diye hatayi yakalar ve o istemciyi kapatir.
    /// </summary>
    private async Task SafeSendAsync(ClientSession session, ChatPacket packet)
    {
        try
        {
            await session.Connection.SendAsync(packet).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            OnLog($"{session.Info.UserName} kullanicisina gonderilemedi: {ex.Message}");
            session.Connection.Dispose();
        }
    }

    private void OnLog(string message) => Log?.Invoke(this, message);

    public void Dispose()
    {
        if (IsRunning)
            StopAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Istemci ayrilma olayinin verileri.
/// </summary>
public class ClientDisconnectedEventArgs(ConnectedClientInfo client, string reason) : EventArgs
{
    public ConnectedClientInfo Client { get; } = client;
    public string Reason { get; } = reason;
}
