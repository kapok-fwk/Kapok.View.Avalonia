using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Kapok.View.Avalonia.ValueConverter;

namespace Kapok.View.Avalonia.DefaultPageControls;

/// <summary>
/// A plain, flat toolbar rendering an IInteractivePage's Base menu (tabs -> groups -> leaf action
/// items, flattened into one row of buttons - no tabs/ribbon chrome). Stands in for
/// Kapok.View.Wpf's Ribbon + MenuItemTemplateSelector in this phase, before AvaloniaControls.Ribbon
/// is wired in.
///
/// Only IAction/IToggleAction leaf items are rendered (UIMenuItemAction/UIToggleMenuItemAction).
/// IDataSetSelectionAction&lt;TEntry&gt;-backed items (UIMenuItemDataSetSelectionAction&lt;TEntry&gt;) are
/// skipped for now - they need a selection list supplied via InteractiveMenu.SelectedItemsBinding
/// and per-closed-generic-type command wiring that the Ribbon phase's richer menu renderer will
/// cover; the important actions ToDoAvaloniaApp exercises (open pages, save, refresh, create,
/// delete via keyboard) don't depend on it for this verification pass.
/// </summary>
public class MenuToolbar : WrapPanel
{
    public MenuToolbar()
    {
        Orientation = Orientation.Horizontal;
        AttachedToVisualTree += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();

        // Menu is only exposed on the concrete InteractivePage class, not the IInteractivePage
        // interface.
        if (DataContext is not InteractivePage page)
            return;

        if (!page.Menu.TryGetValue(UIMenu.BaseMenuName, out var baseMenu))
            return;

        foreach (var tab in baseMenu.MenuItems)
        {
            foreach (var group in tab.SubMenuItems)
            {
                foreach (var item in group.SubMenuItems)
                {
                    var control = BuildControl(item);
                    if (control != null)
                        Children.Add(control);
                }
            }
        }
    }

    private static Control? BuildControl(UIMenuItem item)
    {
        switch (item)
        {
            case UIToggleMenuItemAction toggleAction:
            {
                var button = new ToggleButton
                {
                    Content = new CaptionConverter().Convert(toggleAction.Label, typeof(string), null, System.Globalization.CultureInfo.CurrentUICulture),
                    Command = new ActionCommand(toggleAction),
                    Margin = new global::Avalonia.Thickness(2)
                };
                button.Bind(ToggleButton.IsCheckedProperty, new global::Avalonia.Data.Binding(nameof(UIToggleMenuItemAction.IsChecked)) { Source = toggleAction });
                button[!IsVisibleProperty] = new global::Avalonia.Data.Binding(nameof(UIMenuItem.IsVisible)) { Source = item };
                return button;
            }
            case UIMenuItemAction action:
            {
                var button = new Button
                {
                    Content = new CaptionConverter().Convert(action.Label, typeof(string), null, System.Globalization.CultureInfo.CurrentUICulture),
                    Command = new ActionCommand(action),
                    Margin = new global::Avalonia.Thickness(2)
                };
                button[!IsVisibleProperty] = new global::Avalonia.Data.Binding(nameof(UIMenuItem.IsVisible)) { Source = item };
                return button;
            }
            default:
                // Nested submenus / IDataSetSelectionAction<T> items - not yet rendered, see class summary.
                return null;
        }
    }
}
