namespace ChatApp.Core.Protocol;

/// <summary>
/// Kullaniciya dogrudan gosterilebilecek, anlasilir mesajli sohbet hatasi.
/// </summary>
public class ChatException : Exception
{
    public ChatException(string message) : base(message) { }

    public ChatException(string message, Exception inner) : base(message, inner) { }
}
