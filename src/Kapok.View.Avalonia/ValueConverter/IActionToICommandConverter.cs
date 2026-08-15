using System.Globalization;
using Avalonia.Data.Converters;

namespace Kapok.View.Avalonia.ValueConverter;

/// <summary>
/// Converts a Kapok <see cref="IAction"/> into a bindable ICommand (<see cref="ActionCommand"/>),
/// mirroring Kapok.View.Wpf's IActionToICommandConverter. Only handles the plain IAction case -
/// generic IAction&lt;T&gt; actions (e.g. IDataSetSelectionAction&lt;TEntry&gt;) are wired directly via
/// ActionCommand.ForGeneric&lt;T&gt; where they're constructed in code, since XAML converters can't
/// see the closed generic type at bind time.
/// </summary>
public class IActionToICommandConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            null => null,
            IAction action => new ActionCommand(action),
            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
