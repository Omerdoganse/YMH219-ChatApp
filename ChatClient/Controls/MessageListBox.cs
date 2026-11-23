using System.ComponentModel;
using ChatApp.Core.Models;

namespace ChatClient.Controls;

/// <summary>
/// ChatPacket nesnelerini turune gore renklendirerek ve uzun satirlari kaydirarak gosteren ozel ListBox.
/// ListBox sinifindan kalitim alir; OnDrawItem ve OnMeasureItem metotlarini override eder.
/// </summary>
public class MessageListBox : ListBox
{
    private const int VerticalPadding = 4;
    private const int HorizontalPadding = 6;

    public MessageListBox()
    {
        DrawMode = DrawMode.OwnerDrawVariable;
        IntegralHeight = false;
        BackColor = Color.White;
        BorderStyle = BorderStyle.FixedSingle;
        DoubleBuffered = true;
    }

    /// <summary>Kendi mesajlarimizi farkli renkte gostermek icin oturumdaki kullanici adi.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? CurrentUserName { get; set; }

    /// <summary>Yeni mesaj ekler ve listeyi en alta kaydirir.</summary>
    public void AddMessage(ChatPacket packet)
    {
        Items.Add(packet);
        TopIndex = Math.Max(0, Items.Count - 1);
    }

    /// <summary>Listedeki tum mesajlar (gecmis kaydetme icin).</summary>
    public IEnumerable<ChatPacket> Messages => Items.OfType<ChatPacket>();

    protected override void OnMeasureItem(MeasureItemEventArgs e)
    {
        base.OnMeasureItem(e);
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not ChatPacket packet)
            return;

        int width = Math.Max(50, ClientSize.Width - 2 * HorizontalPadding - SystemInformation.VerticalScrollBarWidth);
        Size size = TextRenderer.MeasureText(packet.ToDisplayString(), GetFont(packet),
            new Size(width, int.MaxValue), TextFormatFlags.WordBreak);
        e.ItemHeight = Math.Min(255, size.Height + 2 * VerticalPadding);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not ChatPacket packet)
            return;

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color back = selected ? Color.FromArgb(220, 232, 252) : GetBackColor(packet);
        using (var brush = new SolidBrush(back))
            e.Graphics.FillRectangle(brush, e.Bounds);

        var textBounds = Rectangle.Inflate(e.Bounds, -HorizontalPadding, -VerticalPadding);
        TextRenderer.DrawText(e.Graphics, packet.ToDisplayString(), GetFont(packet), textBounds,
            GetForeColor(packet), TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // Genislik degisince satir yukseklikleri yeniden hesaplansin
        if (DrawMode == DrawMode.OwnerDrawVariable && Items.Count > 0)
        {
            object[] items = Items.Cast<object>().ToArray();
            BeginUpdate();
            Items.Clear();
            Items.AddRange(items);
            EndUpdate();
            TopIndex = Math.Max(0, Items.Count - 1);
        }
    }

    private bool IsOwn(ChatPacket p) =>
        CurrentUserName is not null && p.Sender.Equals(CurrentUserName, StringComparison.OrdinalIgnoreCase);

    // Her cizimde yeni Font olusturmamak icin italik/kalin yazi tipleri onbellekte tutulur.
    private Font? _italicFont;
    private Font? _boldFont;

    protected override void OnFontChanged(EventArgs e)
    {
        _italicFont?.Dispose(); _italicFont = null;
        _boldFont?.Dispose(); _boldFont = null;
        base.OnFontChanged(e);
    }

    private Font GetFont(ChatPacket p) =>
        p.Type is PacketType.System or PacketType.JoinAccepted or PacketType.JoinRejected
            ? _italicFont ??= new Font(Font, FontStyle.Italic)
            : IsOwn(p) ? _boldFont ??= new Font(Font, FontStyle.Bold) : Font;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _italicFont?.Dispose();
            _boldFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    private Color GetForeColor(ChatPacket p) => p.Type switch
    {
        PacketType.System or PacketType.JoinAccepted or PacketType.JoinRejected => Color.DimGray,
        PacketType.PrivateMessage => Color.Purple,
        _ when IsOwn(p) => Color.FromArgb(0, 70, 160),
        _ => Color.Black
    };

    private static Color GetBackColor(ChatPacket p) => p.Type switch
    {
        PacketType.PrivateMessage => Color.FromArgb(248, 240, 252),
        PacketType.System or PacketType.JoinAccepted or PacketType.JoinRejected => Color.FromArgb(246, 246, 246),
        _ => Color.White
    };
}
