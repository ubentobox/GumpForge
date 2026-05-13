using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GumpForge.App.Converters;

/// <summary>
/// Converts a color tag string (e.g. "#e94560", "red", "") to a SolidColorBrush.
/// Returns Transparent when the tag is empty.
/// </summary>
public class ColorTagToBrushConverter : IValueConverter
{
    public static readonly ColorTagToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string tag && !string.IsNullOrWhiteSpace(tag))
        {
            try { return new SolidColorBrush(Color.Parse(tag)); }
            catch { return Brushes.Transparent; }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns true if the string is not null/empty (for IsVisible binding).
/// </summary>
public class StringNotEmptyConverter : IValueConverter
{
    public static readonly StringNotEmptyConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
