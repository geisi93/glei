using System.Globalization;
using Avalonia.Data.Converters;

namespace TriasDeparture.Linux.Popup;

/// <summary>☆ / ★ for "Als Favorit" / "Dein Favorit".</summary>
public sealed class StarConverter(string off, string on) : IValueConverter
{
    public static readonly StarConverter Instance = new("☆", "★");
    public static readonly StarConverter ToolTip = new("Als Favorit", "Dein Favorit");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? on : off;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
