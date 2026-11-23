using System.Net.Sockets;
using ChatApp.Core.Models;
using ChatApp.Core.Protocol;

namespace ChatApp.Core.Network;

/// <summary>
/// TcpClient ile sunucuya baglanan, mesaj gonderen ve gelen mesajlari olay olarak yayinlayan istemci servisi.
/// Olaylar arka plan is parcaciklarindan tetiklenir; arayuz tarafinda Invoke ile isaretlenmelidir.
/// </summary>
public sealed class ChatClientService : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private PacketConnection? _connection;
    private CancellationTokenSource? _cts;
    private volatile bool _userRequestedDisconnect;

    /// <summary>Sunucuya bagli mi?</summary>
    public bool IsConnected { get; private set; }

    /// <summary>Sunucunun kabul ettigi kullanici adi.</summary>
    public string? UserName { get; private set; }

    /// <summary>Bagli olunan sunucu (host:port).</summary>
    public string? ServerAddress { get; private set; }

    /// <summary>Sunucudan gelen sohbet/sistem mesajlari.</summary>
    public event EventHandler<ChatPacket>? PacketReceived;

    /// <summary>Cevrimici kullanici listesi guncellendiginde.</summary>
    public event EventHandler<IReadOnlyList<string>>? UserListReceived;

    /// <summary>Baglanti herhangi bir nedenle sona erdiginde (parametre: neden).</summary>
    public event EventHandler<string>? Disconnected;

    /// <summary>
    /// Sunucuya baglanir ve kullanici adiyla katilir.
    /// Hata durumunda anlasilir mesajli ChatException firlatir.
    /// </summary>
    public async Task ConnectAsync(string host, int port, string userName)
    {
        if (IsConnected)
            throw new ChatException("Zaten bir sunucuya baglisiniz.");
        if (string.IsNullOrWhiteSpace(host))
            throw new ChatException("Sunucu adresi bos olamaz.");
        if (!ChatRules.IsValidPort(port))
            throw new ChatException("Port 1024 ile 65535 arasinda olmalidir.");
        if (ChatRules.ValidateUserName(userName) is { } nameError)
            throw new ChatException(nameError);

        host = host.Trim();
        userName = userName.Trim();
        var tcp = new TcpClient();
        PacketConnection? connection = null;

        try
        {
            using var timeout = new CancellationTokenSource(ConnectTimeout);
            await tcp.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);

            connection = new PacketConnection(tcp);
            await connection.SendAsync(ChatPacket.Join(userName), timeout.Token).ConfigureAwait(false);
            ChatPacket? reply = await connection.ReceiveAsync(timeout.Token).ConfigureAwait(false);

            if (reply is null)
                throw new ChatException("Sunucu baglantiyi kapatti.");
            if (reply.Type == PacketType.JoinRejected)
                throw new ChatException("Sunucu katilmayi reddetti: " + reply.Text);
            if (reply.Type != PacketType.JoinAccepted)
                throw new ChatException("Sunucudan beklenmeyen yanit alindi.");

            _connection = connection;
            _cts = new CancellationTokenSource();
            _userRequestedDisconnect = false;
            UserName = userName;
            ServerAddress = $"{host}:{port}";
            IsConnected = true;

            PacketReceived?.Invoke(this, reply);
            _ = ReceiveLoopAsync(connection, _cts.Token);
        }
        catch (ChatException)
        {
            Cleanup(connection, tcp);
            throw;
        }
        catch (OperationCanceledException)
        {
            Cleanup(connection, tcp);
            throw new ChatException($"Sunucuya {ConnectTimeout.TotalSeconds:0} saniye icinde baglanilamadi ({host}:{port}).");
        }
        catch (SocketException ex)
        {
            Cleanup(connection, tcp);
            throw new ChatException($"Sunucuya baglanilamadi ({host}:{port}). Sunucunun calistigindan emin olun.\n{ex.Message}", ex);
        }
        catch (IOException ex)
        {
            Cleanup(connection, tcp);
            throw new ChatException("Baglanti sirasinda veri aktarim hatasi olustu: " + ex.Message, ex);
        }
    }

    /// <summary>Herkese acik mesaj gonderir.</summary>
    public Task SendMessageAsync(string text) =>
        SendAsync(ChatPacket.Message(UserName ?? string.Empty, text?.Trim() ?? string.Empty));

    /// <summary>Yalnizca hedef kullaniciya ozel mesaj gonderir.</summary>
    public Task SendPrivateMessageAsync(string targetUser, string text)
    {
        if (string.IsNullOrWhiteSpace(targetUser))
            throw new ChatException("Ozel mesaj icin bir alici seciniz.");
        if (targetUser.Equals(UserName, StringComparison.OrdinalIgnoreCase))
            throw new ChatException("Kendinize ozel mesaj gonderemezsiniz.");
        return SendAsync(ChatPacket.Private(UserName ?? string.Empty, targetUser, text?.Trim() ?? string.Empty));
    }

    private async Task SendAsync(ChatPacket packet)
    {
        PacketConnection? connection = _connection;
        if (!IsConnected || connection is null)
            throw new ChatException("Mesaj gondermek icin once sunucuya baglanin.");
        if (ChatRules.ValidateMessage(packet.Text) is { } error)
            throw new ChatException(error);

        try
        {
            await connection.SendAsync(packet).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            throw new ChatException("Mesaj gonderilemedi, sunucu ile baglanti kopmus olabilir.", ex);
        }
    }

    /// <summary>
    /// Sunucuya ayrilma bildirimi gonderip baglantiyi kapatir.
    /// </summary>
    public async Task DisconnectAsync()
    {
        PacketConnection? connection = _connection;
        if (!IsConnected || connection is null)
            return;

        _userRequestedDisconnect = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await connection.SendAsync(ChatPacket.Leave(UserName ?? string.Empty), timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sunucu zaten kapali olabilir; yine de yerel baglantiyi kapatiyoruz.
        }

        _cts?.Cancel();
        connection.Dispose();
    }

    private async Task ReceiveLoopAsync(PacketConnection connection, CancellationToken token)
    {
        string reason = "Sunucu baglantiyi kapatti.";
        try
        {
            while (!token.IsCancellationRequested)
            {
                ChatPacket? packet = await connection.ReceiveAsync(token).ConfigureAwait(false);
                if (packet is null)
                    break;

                if (packet.Type == PacketType.UserList)
                    UserListReceived?.Invoke(this, packet.Users ?? []);
                else
                    PacketReceived?.Invoke(this, packet);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            reason = "Sunucu ile baglanti koptu.";
        }
        finally
        {
            if (_userRequestedDisconnect)
                reason = "Baglanti kesildi.";

            IsConnected = false;
            connection.Dispose();
            _connection = null;
            _cts = null; // zamanlayicisiz CTS; Dispose gerekmez, yaris durumunu da onler

            Disconnected?.Invoke(this, reason);
        }
    }

    private static void Cleanup(PacketConnection? connection, TcpClient tcp)
    {
        if (connection is not null)
            connection.Dispose();
        else
            tcp.Dispose();
    }

    public void Dispose()
    {
        _userRequestedDisconnect = true;
        _cts?.Cancel();
        _connection?.Dispose();
    }
}
