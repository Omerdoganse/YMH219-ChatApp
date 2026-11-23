using System.Text;
using ChatApp.Core.Protocol;

namespace ChatApp.Core.Services;

/// <summary>
/// Sohbet gecmisini veya sunucu gunlugunu duz metin (.txt) dosyasina kaydeder.
/// Dosya hatalari anlasilir mesajli ChatException'a donusturulur.
/// </summary>
public static class ChatHistoryService
{
    /// <summary>
    /// Ornek: sohbet_gecmisi_20261123_181530.txt
    /// </summary>
    public static string CreateDefaultFileName(string prefix) =>
        $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

    /// <summary>
    /// Satirlari basliklariyla birlikte UTF-8 olarak dosyaya yazar ve yazilan satir sayisini dondurur.
    /// </summary>
    public static async Task<int> SaveAsync(string filePath, string title, IEnumerable<string> lines)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ChatException("Kayit icin bir dosya yolu seciniz.");

        List<string> content = lines.ToList();
        if (content.Count == 0)
            throw new ChatException("Kaydedilecek mesaj bulunmuyor.");

        var builder = new StringBuilder();
        builder.AppendLine($"# {title}");
        builder.AppendLine($"# Kayit zamani: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        builder.AppendLine($"# Satir sayisi: {content.Count}");
        builder.AppendLine();
        foreach (string line in content)
            builder.AppendLine(line);

        try
        {
            await File.WriteAllTextAsync(filePath, builder.ToString(), new UTF8Encoding(true)).ConfigureAwait(false);
            return content.Count;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ChatException("Bu konuma yazma izniniz yok. Baska bir klasor seciniz.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new ChatException("Secilen klasor bulunamadi.", ex);
        }
        catch (IOException ex)
        {
            throw new ChatException("Dosya kaydedilemedi (dosya baska bir programda acik olabilir).\n" + ex.Message, ex);
        }
    }
}
