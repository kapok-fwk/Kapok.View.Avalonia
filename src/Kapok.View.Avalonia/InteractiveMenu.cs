using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace Kapok.View.Avalonia;

/// <summary>
/// Direct port of Kapok.View.Wpf's InteractiveMenu attached property - Avalonia has attached
/// properties too, same concept. Set on a window/control to tell the toolbar/ribbon which
/// selection list (e.g. DataSet.SelectedEntries) an IDataSetSelectionAction-backed menu item
/// should act on.
/// </summary>
public static class InteractiveMenu
{
    public static readonly AttachedProperty<IList?> SelectedItemsBindingProperty =
        AvaloniaProperty.RegisterAttached<StyledElement, IList?>("SelectedItemsBinding", typeof(InteractiveMenu));

    public static void SetSelectedItemsBinding(StyledElement element, IList? value)
        => element.SetValue(SelectedItemsBindingProperty, value);

    public static IList? GetSelectedItemsBinding(StyledElement element)
        => element.GetValue(SelectedItemsBindingProperty);
}
