using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media.Imaging;
using AvaloniaUI.Ribbon;
using AvaloniaUI.Ribbon.Desktop;
using Kapok.View.Avalonia.ValueConverter;

namespace Kapok.View.Avalonia.DefaultPageControls;

/// <summary>
/// Builds a DesktopRibbon's Tabs/Groups/buttons from an InteractivePage's Base menu, mirroring
/// Kapok.View.Wpf's WindowStyling.xaml (InteractWindowRibbonTabStyle/RibbonGroupStyle) +
/// MenuItemTemplateSelector.cs. AvaloniaControls.Ribbon has no XAML ItemsSource/
/// DataTemplateSelector-driven binding model like WPF's Ribbon does - Ribbon.Tabs/RibbonTab.Groups
/// are plain ObservableCollections meant to be populated directly - so this is a code-first
/// builder (extending the same imperative pattern Phase 1's MenuToolbar.cs established) rather
/// than a template selector port.
///
/// Static structure only: unlike WPF's ItemsSource binding, this does not react to
/// UIMenuItem.SubMenuItems being added/removed after the initial build (same simplification
/// MenuToolbar.cs already made in Phase 1 - ToDoAvaloniaApp's menus are static after construction).
/// Per-item IsVisible still updates reactively via a live binding.
/// </summary>
public static class RibbonMenuBuilder
{
    public static void Build(DesktopRibbon ribbon, InteractivePage page)
    {
        ribbon.Tabs.Clear();

        if (!page.Menu.TryGetValue(UIMenu.BaseMenuName, out var baseMenu))
            return;

        foreach (var tabItem in baseMenu.MenuItems)
        {
            var ribbonTab = new RibbonTab
            {
                Header = Caption(tabItem.Label)
            };
            BindIsVisible(ribbonTab, tabItem);
            SetKeyTip(ribbonTab, tabItem.RibbonKeyTip);

            if (tabItem is UIMenuItemTab tab)
            {
                ribbonTab.Bind(TabItem.IsSelectedProperty,
                    new Binding(nameof(UIMenuItemTab.IsSelected)) { Source = tab, Mode = BindingMode.TwoWay });
            }

            foreach (var groupItem in tabItem.SubMenuItems)
            {
                var ribbonGroup = new RibbonGroupBox
                {
                    Header = Caption(groupItem.Label)
                };
                BindIsVisible(ribbonGroup, groupItem);
                SetKeyTip(ribbonGroup, groupItem.RibbonKeyTip);

                foreach (var leaf in groupItem.SubMenuItems)
                {
                    var control = BuildLeaf(leaf);
                    if (control != null)
                        ribbonGroup.Items.Add(control);
                }

                ribbonTab.Groups.Add(ribbonGroup);
            }

            ribbon.Tabs.Add(ribbonTab);
        }
    }

    private static Control? BuildLeaf(UIMenuItem item)
    {
        // UIMenuItemDataSetSelectionAction<TEntry> is a closed generic type only known at
        // runtime - matches WPF's MenuItemTemplateSelector check against GetGenericTypeDefinition().
        if (item.GetType().IsGenericType &&
            item.GetType().GetGenericTypeDefinition() == typeof(UIMenuItemDataSetSelectionAction<>))
        {
            return BuildTableDataButton(item);
        }

        switch (item)
        {
            case UIToggleMenuItemAction toggleAction:
                return BuildToggleButton(toggleAction);
            case UIMenuItemAction action:
                return BuildButton(action);
            default:
                // A UIMenuItem with children but no action of its own - matches WPF's
                // MenuButtonTemplate (RibbonMenuButton). No submenu is exercised by
                // ToDoAvaloniaApp's Base menu today, so this path is a best-effort direct port,
                // not live-verified against a real running submenu.
                return item.SubMenuItems.Count > 0 ? BuildDropDown(item) : null;
        }
    }

    private static RibbonButton BuildButton(UIMenuItemAction action)
    {
        var button = new RibbonButton
        {
            Content = Caption(action.Label),
            Command = new ValueConverter.ActionCommand(action),
            [ToolTip.TipProperty] = Caption(action.Description)
        };
        SetIcons(button, action.Image, action.ImageIsBig);
        BindIsVisible(button, action);
        SetKeyTip(button, action.RibbonKeyTip);
        return button;
    }

    private static RibbonToggleButton BuildToggleButton(UIToggleMenuItemAction toggleAction)
    {
        var button = new RibbonToggleButton
        {
            Content = Caption(toggleAction.Label),
            Command = new ValueConverter.ActionCommand(toggleAction),
            [ToolTip.TipProperty] = Caption(toggleAction.Description)
        };
        button.Bind(ToggleButton.IsCheckedProperty,
            new Binding(nameof(UIToggleMenuItemAction.IsChecked)) { Source = toggleAction });
        SetIcons(button, toggleAction.Image, toggleAction.ImageIsBig);
        BindIsVisible(button, toggleAction);
        SetKeyTip(button, toggleAction.RibbonKeyTip);
        return button;
    }

    /// <summary>
    /// Matches WPF's TableDataButtonTemplate: a RibbonButton whose CommandParameter is the
    /// referencing data set's current selection, wrapped via ActionCommand.ForGeneric&lt;TEntry&gt;
    /// (the closed generic type is only known at runtime, so this goes through reflection once
    /// per button build - not per click). Genuinely exercised by ToDoAvaloniaApp - TaskLists'
    /// OpenTasksAction is an IDataSetSelectionAction&lt;TaskList&gt; - confirmed via a real headless
    /// render: renders as a "Tasks" button in the Manage group with a working icon.
    /// </summary>
    private static RibbonButton BuildTableDataButton(UIMenuItem item)
    {
        var entryType = item.GetType().GetGenericArguments()[0];
        var actionProperty = item.GetType().GetProperty("Action", BindingFlags.Public | BindingFlags.Instance)!;
        var action = actionProperty.GetValue(item)!;

        var forGeneric = typeof(ValueConverter.ActionCommand)
            .GetMethod(nameof(ValueConverter.ActionCommand.ForGeneric))!
            .MakeGenericMethod(typeof(IList<>).MakeGenericType(entryType));
        var command = (System.Windows.Input.ICommand)forGeneric.Invoke(null, new[] { action })!;

        var button = new RibbonButton
        {
            Content = Caption(item.Label),
            Command = command,
            [ToolTip.TipProperty] = Caption(item.Description)
        };
        // ReferencingDataSet is declared directly on the closed UIMenuItemDataSetSelectionAction<TEntry>
        // type, not on any interface the generic-erased UIMenuItem reference exposes - a plain
        // string binding path resolves it at runtime same as WPF's PriorityBinding did.
        button.Bind(RibbonButton.CommandParameterProperty,
            new Binding("ReferencingDataSet.SelectedEntries") { Source = item });
        SetIcons(button, item.Image, item.ImageIsBig);
        BindIsVisible(button, item);
        SetKeyTip(button, item.RibbonKeyTip);
        return button;
    }

    private static RibbonDropDownButton BuildDropDown(UIMenuItem item)
    {
        var dropDown = new RibbonDropDownButton
        {
            Content = Caption(item.Label),
            [ToolTip.TipProperty] = Caption(item.Description)
        };
        SetIcons(dropDown, item.Image, item.ImageIsBig);
        BindIsVisible(dropDown, item);

        foreach (var child in item.SubMenuItems)
        {
            if (child is UIMenuItemAction childAction)
            {
                var dropDownItem = new RibbonDropDownItem
                {
                    Header = Caption(childAction.Label),
                    Command = new ValueConverter.ActionCommand(childAction)
                };
                BindIsVisible(dropDownItem, childAction);
                dropDown.Items.Add(dropDownItem);
            }
        }

        return dropDown;
    }

    private static void SetIcons(Control control, string? image, bool? imageIsBig)
    {
        if (image == null)
            return;

        var converter = new ImageNameToImageSourceConverter();

        var small = (Bitmap?)converter.Convert(image, typeof(Bitmap), "Small", CultureInfo.CurrentUICulture);
        if (small != null)
            control.SetValue(RibbonButton.IconProperty, new Image { Source = small, Width = 16, Height = 16 });

        // Matches WPF's LargeImageSource MultiBinding: ImageIsBig == false suppresses the large icon.
        if (imageIsBig != false)
        {
            var large = (Bitmap?)converter.Convert(image, typeof(Bitmap), "Large", CultureInfo.CurrentUICulture);
            if (large != null)
                control.SetValue(RibbonButton.LargeIconProperty, new Image { Source = large, Width = 32, Height = 32 });
        }
    }

    private static void BindIsVisible(Control control, UIMenuItem item)
    {
        control.Bind(Control.IsVisibleProperty, new Binding(nameof(UIMenuItem.IsVisible)) { Source = item });
    }

    private static void SetKeyTip(Control control, string? keyTip)
    {
        if (!string.IsNullOrEmpty(keyTip))
            control.SetValue(KeyTip.KeyTipKeysProperty, keyTip);
    }

    private static string? Caption(Kapok.Caption caption)
        => (string?)new CaptionConverter().Convert(caption, typeof(string), null, CultureInfo.CurrentUICulture);
}
