using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Kapok.View.Avalonia.ValueConverter;

/// <summary>
/// Turns an <c>IHierarchyEntry&lt;T&gt;.Level</c> into the indent margin for one part of a tree
/// column's cell. Direct port of Kapok.View.Wpf's DataGridHierarchyColumnLevelToMarginConverter
/// (its file is named "DataGridHierachyColumnLevelToMarginConverter.cs" there - a typo in the file
/// name only, the class itself is spelled correctly); the only change is
/// <c>System.Windows.Thickness</c> becoming <c>Avalonia.Thickness</c>, the arithmetic is identical.
/// </summary>
public class DataGridHierarchyColumnLevelToMarginConverter : IValueConverter
{
    /// <summary>Indent, in device-independent pixels, added per hierarchy level.</summary>
    public const double LevelIndent = 20;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int level)
            return null;

        if (parameter == null)
            return new Thickness(LevelIndent * level, 0, 0, 0);

        return parameter.ToString() switch
        {
            "HLine" => new Thickness(LevelIndent * level + 9, 1, 0, 0),
            "VLine" => new Thickness(LevelIndent * level, 0, 0, 0),
            // "ToggleButton" and anything else
            _ => new Thickness(LevelIndent * level, 1, 0, 0)
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
