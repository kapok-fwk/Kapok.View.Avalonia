using System.Globalization;
using Avalonia.Data.Converters;
using Kapok.Entity.Model;

namespace Kapok.View.Avalonia.ValueConverter;

/// <summary>
/// Resolves a Kapok <see cref="Caption"/> to the current-culture display string. Direct port of
/// Kapok.View.Wpf's CaptionConverter - Caption is a plain Kapok.Core type, not WPF-specific.
/// </summary>
public class CaptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            null => null,
            Caption caption => caption.LanguageOrDefault(culture) ?? caption.LanguageOrDefault(CultureInfo.InvariantCulture),
            _ => value
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
