using System.Text.Json;
using System.Text.Json.Serialization;
using ChatApp.Core.Models;

namespace ChatApp.Core.Protocol;

/// <summary>
/// Paketleri tek satirlik JSON metnine cevirir ve geri okur.
/// Satir sonu (\n) paket ayiraci olarak kullanildigi icin JSON tek satir uretilir.
/// </summary>
public static class PacketSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(ChatPacket packet) =>
        JsonSerializer.Serialize(packet, Options);

    /// <summary>
    /// Bir satiri pakete cevirir. Bozuk veri gelirse null doner (uygulama cokmez).
    /// </summary>
    public static ChatPacket? TryDeserialize(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ChatPacket>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
