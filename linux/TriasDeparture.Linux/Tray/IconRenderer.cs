using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace TriasDeparture.Linux.Tray;

/// <summary>
/// Draws the minutes as tray icon. Tray hosts (StatusNotifierItem) scale the pixmap to the panel size,
/// so it is rendered once in a generous size.
/// </summary>
public static class IconRenderer
{
    public const int IconSize = 64;
    private static readonly Color RealtimeColor = Color.FromRgb(0xFB, 0xBF, 0x24);

    /// <summary>
    /// True if the panel is light (then the number is drawn black). Panels are dark by default on GNOME, Cinnamon,
    /// MATE, Budgie and Xfce, even with a light app theme; only KDE's panel follows the color scheme.
    /// <paramref name="overrideValue"/> is $GLEI_PANEL ("light"/"dark") for setups that guess wrong.
    /// The digits get a contrasting outline either way, so a wrong guess stays readable.
    /// </summary>
    public static bool IsPanelLight(string? currentDesktop, ThemeVariant? systemTheme, string? overrideValue = null) =>
        overrideValue?.Trim().ToLowerInvariant() switch
        {
            "light" => true,
            "dark"  => false,
            _       => IsDesktop(currentDesktop, "KDE") && systemTheme == ThemeVariant.Light
        };

    public static bool IsGnome(string? currentDesktop) => IsDesktop(currentDesktop, "GNOME");

    private static bool IsDesktop(string? currentDesktop, string name) =>
        (currentDesktop ?? "").Split(':').Any(d => d.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static WindowIcon Render(string text, bool lightPanel, bool dimmed, bool realtimeDot)
    {
        const int size = IconSize;
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            var color = lightPanel ? Colors.Black : Colors.White;
            var halo  = lightPanel ? Colors.White : Colors.Black;
            if (dimmed) color = Color.FromArgb(130, color.R, color.G, color.B);
            var brush = new SolidColorBrush(color);
            // Thin contrasting outline: readable even when the panel color was guessed wrong
            var outline   = size * 0.07;
            var haloPen   = new Pen(new SolidColorBrush(Color.FromArgb(dimmed ? (byte)70 : (byte)140, halo.R, halo.G, halo.B)), outline,
                lineJoin: PenLineJoin.Round);
            var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

            // The realtime dot sits in the bottom-right corner; the digits keep clear of it
            var dot   = size * 0.22;
            var width = (realtimeDot ? size - dot * 0.8 : size) - outline;

            // As big as possible: the ink of the digits fills the free width (and at most the height)
            var em = size * 1.1;
            FormattedText formatted;
            Rect ink;
            while (true)
            {
                formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, em, brush);
                // Ink bounds, not the line box: digits have no descender and some side bearing
                ink = formatted.BuildGeometry(new Point(0, 0))?.Bounds ?? new Rect(0, 0, formatted.Width, formatted.Height);
                if ((ink.Width <= width && ink.Height <= size - outline) || em < 6)
                    break;
                em -= 1;
            }

            var origin = new Point((width + outline - ink.Width) / 2 - ink.X, (size - ink.Height) / 2 - ink.Y);
            if (formatted.BuildGeometry(origin) is { } glyphs)
            {
                ctx.DrawGeometry(null, haloPen, glyphs);
                ctx.DrawGeometry(brush, null, glyphs);
            }
            else
            {
                ctx.DrawText(formatted, origin);
            }

            if (realtimeDot)
                ctx.DrawEllipse(new SolidColorBrush(RealtimeColor), null, new Rect(size - dot, size - dot, dot, dot));
        }

        using var png = new MemoryStream();
        bitmap.Save(png, new PngBitmapEncoderOptions());
        png.Position = 0;
        return new WindowIcon(png);
    }

    /// <summary>The glei app icon (shown while no favorite is set).</summary>
    public static WindowIcon AppIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://glei/Assets/glei.ico"));
        return new WindowIcon(stream);
    }
}
