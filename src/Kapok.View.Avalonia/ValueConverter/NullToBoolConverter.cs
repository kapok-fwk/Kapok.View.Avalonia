using System.Globalization;
using Avalonia.Data.Converters;

namespace Kapok.View.Avalonia.ValueConverter;

/// <summary>
/// Converts null to boolean: null -&gt; true, non-null -&gt; false. Matches Kapok.View.Wpf's
/// NullToBoolValueConverter (its no-parameter case; WPF's parameter-inverts-the-result behaviour
/// has no XAML-parameter equivalent worth keeping here, since <see cref="InverseNullToBoolConverter"/>
/// already covers that case as its own type). Found and fixed while porting Kapok.View.Wpf.UnitTest's
/// converter test: an earlier version of this file had the two classes' results swapped relative to
/// WPF's - unused anywhere in this port yet, so no behavioural impact today, but would have bitten
/// the first XAML binding written against it expecting WPF-identical semantics.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value == null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts null to boolean, inverted: null -&gt; false, non-null -&gt; true. Matches
/// Kapok.View.Wpf's InverseNullToBoolConverter (its no-parameter case) - see
/// <see cref="NullToBoolConverter"/>'s remarks on the swapped-semantics bug this fixed.
/// </summary>
public class InverseNullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
