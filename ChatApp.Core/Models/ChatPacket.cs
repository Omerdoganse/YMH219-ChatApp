namespace ChatApp.Core.Models;

/// <summary>
/// Ag uzerinden gonderilen tek bir paket. Her paket JSON olarak tek satirda tasinir.
/// </summary>
public class ChatPacket
{
    public PacketType Type { get; set; }

    /// <summary>Gonderen kullanici adi (sistem mesajlarinda "Sunucu").</summary>
    public string Sender { get; set; } = string.Empty;

    /// <summary>Ozel mesajlarda alici kullanici adi; diger turlerde bos.</summary>
    public string? Target { get; set; }

    /// <summary>Mesaj metni veya aciklama.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Mesajin olusturuldugu zaman.</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>UserList paketinde cevrimici kullanici adlari.</summary>
    public List<string>? Users { get; set; }

    // --- Kolay olusturma icin fabrika metotlari ---

    public static ChatPacket Join(string userName) =>
        new() { Type = PacketType.Join, Sender = userName };

    public static ChatPacket Leave(string userName) =>
        new() { Type = PacketType.Leave, Sender = userName };

    public static ChatPacket Message(string sender, string text) =>
        new() { Type = PacketType.Message, Sender = sender, Text = text };

    public static ChatPacket Private(string sender, string target, string text) =>
        new() { Type = PacketType.PrivateMessage, Sender = sender, Target = target, Text = text };

    public static ChatPacket SystemNotice(string text) =>
        new() { Type = PacketType.System, Sender = "Sunucu", Text = text };

    public static ChatPacket UserListOf(IEnumerable<string> users) =>
        new() { Type = PacketType.UserList, Sender = "Sunucu", Users = users.ToList() };

    /// <summary>
    /// Mesaji arayuzde gosterilecek bicime cevirir: [14:05:31] Ali: Merhaba
    /// </summary>
    public string ToDisplayString()
    {
        string time = Timestamp.ToString("HH:mm:ss");
        return Type switch
        {
            PacketType.PrivateMessage => $"[{time}] (Ozel) {Sender} -> {Target}: {Text}",
            PacketType.System or PacketType.JoinAccepted or PacketType.JoinRejected
                => $"[{time}] * {Text}",
            _ => $"[{time}] {Sender}: {Text}"
        };
    }
}
