using System.Net;

namespace ChatApp.Core.Models;

/// <summary>
/// Sunucu tarafinda bagli bir istemcinin temel bilgileri (arayuzde listelenir).
/// </summary>
public class ConnectedClientInfo
{
    private static int _nextId;

    public ConnectedClientInfo(string userName, EndPoint? remoteEndPoint)
    {
        Id = Interlocked.Increment(ref _nextId);
        UserName = userName;
        RemoteEndPoint = remoteEndPoint?.ToString() ?? "bilinmiyor";
        ConnectedAt = DateTime.Now;
    }

    /// <summary>Sunucunun verdigi benzersiz numara.</summary>
    public int Id { get; }

    public string UserName { get; }

    /// <summary>Istemcinin IP adresi ve portu (ornegin 127.0.0.1:52144).</summary>
    public string RemoteEndPoint { get; }

    public DateTime ConnectedAt { get; }

    /// <summary>Istemcinin gonderdigi mesaj sayisi.</summary>
    public int MessageCount { get; private set; }

    internal void IncrementMessageCount() => MessageCount++;

    public override string ToString() => $"{UserName} ({RemoteEndPoint})";
}
