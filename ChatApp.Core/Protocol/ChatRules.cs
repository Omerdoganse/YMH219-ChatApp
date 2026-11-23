namespace ChatApp.Core.Protocol;

/// <summary>
/// Sunucu ve istemcinin ortak kullandigi kurallar (kullanici adi, mesaj uzunlugu, varsayilan port).
/// </summary>
public static class ChatRules
{
    public const int DefaultPort = 5050;
    public const int MinUserNameLength = 3;
    public const int MaxUserNameLength = 20;
    public const int MaxMessageLength = 500;

    /// <summary>
    /// Kullanici adini kontrol eder. Gecerliyse null, degilse hata aciklamasi doner.
    /// </summary>
    public static string? ValidateUserName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return "Kullanici adi bos olamaz.";

        string name = userName.Trim();
        if (name.Length < MinUserNameLength || name.Length > MaxUserNameLength)
            return $"Kullanici adi {MinUserNameLength}-{MaxUserNameLength} karakter olmalidir.";

        if (!name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))
            return "Kullanici adinda yalnizca harf, rakam, '_', '-' ve '.' kullanilabilir.";

        if (name.Equals("Sunucu", StringComparison.OrdinalIgnoreCase))
            return "'Sunucu' adi ayrilmistir, baska bir ad seciniz.";

        return null;
    }

    /// <summary>
    /// Mesaj metnini kontrol eder. Gecerliyse null, degilse hata aciklamasi doner.
    /// </summary>
    public static string? ValidateMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Bos mesaj gonderilemez.";
        if (text.Length > MaxMessageLength)
            return $"Mesaj en fazla {MaxMessageLength} karakter olabilir.";
        return null;
    }

    /// <summary>
    /// Port numarasinin gecerli aralikta olup olmadigini kontrol eder.
    /// </summary>
    public static bool IsValidPort(int port) => port is >= 1024 and <= 65535;
}
