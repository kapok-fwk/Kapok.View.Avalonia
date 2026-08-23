using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Kapok.View.Avalonia.ValueConverter;

/// <summary>
/// Converts a <c>byte[]</c> (a <c>[BinaryImage]</c> property's raw image bytes) into an
/// <see cref="Bitmap"/>. Direct port of Kapok.View.Wpf's BinaryImageConverter - same
/// bytes-through-a-MemoryStream approach, <see cref="Bitmap"/> instead of WPF's
/// <c>BitmapImage</c>.
/// </summary>
public class BinaryImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
            return null;

        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
