namespace ChatApp.Core.Models;

/// <summary>
/// Sunucu ile istemci arasinda gidip gelen paketlerin turleri.
/// </summary>
public enum PacketType
{
    /// <summary>Istemci -> Sunucu: kullanici adiyla katilma istegi.</summary>
    Join,
    /// <summary>Sunucu -> Istemci: katilma kabul edildi.</summary>
    JoinAccepted,
    /// <summary>Sunucu -> Istemci: katilma reddedildi (ornegin ayni kullanici adi).</summary>
    JoinRejected,
    /// <summary>Herkese giden genel sohbet mesaji.</summary>
    Message,
    /// <summary>Yalnizca hedef kullaniciya giden ozel mesaj.</summary>
    PrivateMessage,
    /// <summary>Sunucu -> Istemci: cevrimici kullanici listesi.</summary>
    UserList,
    /// <summary>Sunucu bildirimi (katildi/ayrildi/duyuru gibi).</summary>
    System,
    /// <summary>Istemci -> Sunucu: baglantiyi duzgun sekilde kapatma bildirimi.</summary>
    Leave
}
