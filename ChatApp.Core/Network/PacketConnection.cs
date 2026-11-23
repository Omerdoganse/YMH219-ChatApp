using System.Net;
using System.Net.Sockets;
using System.Text;
using ChatApp.Core.Models;
using ChatApp.Core.Protocol;

namespace ChatApp.Core.Network;

/// <summary>
/// Bir TcpClient uzerinde satir tabanli paket gonderme/alma islemlerini kapsuller.
/// StreamReader/StreamWriter kullanir; ayni anda birden fazla gonderim cakismasin diye kilit kullanir.
/// </summary>
public sealed class PacketConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile bool _disposed;

    public PacketConnection(TcpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        RemoteEndPoint = client.Client.RemoteEndPoint;

        NetworkStream stream = client.GetStream();
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _reader = new StreamReader(stream, utf8);
        _writer = new StreamWriter(stream, utf8) { NewLine = "\n", AutoFlush = false };
    }

    /// <summary>Karsi tarafin IP:port bilgisi.</summary>
    public EndPoint? RemoteEndPoint { get; }

    /// <summary>
    /// Paketi JSON satiri olarak gonderir.
    /// </summary>
    public async Task SendAsync(ChatPacket packet, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string line = PacketSerializer.Serialize(packet);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Bir sonraki gecerli paketi bekler. Baglanti kapandiysa null doner.
    /// Bozuk (JSON olmayan) satirlar atlanir.
    /// </summary>
    public async Task<ChatPacket?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            string? line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return null; // karsi taraf baglantiyi kapatti

            ChatPacket? packet = PacketSerializer.TryDeserialize(line);
            if (packet is not null)
                return packet;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Soketi kapatmak bekleyen okuma islemini de sonlandirir.
        try { _client.Close(); } catch { /* kapatirken olusan hatalar onemsiz */ }
        try { _reader.Dispose(); } catch { /* akis zaten kapali olabilir */ }
        try { _writer.Dispose(); } catch { /* akis zaten kapali olabilir */ }
    }
}
