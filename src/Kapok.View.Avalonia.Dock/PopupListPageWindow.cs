using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using AvaloniaUI.Ribbon.Desktop;
using Kapok.View.Avalonia.DefaultPageControls;
using Kapok.View.Avalonia.ValueConverter;
using Kapok.View.Avalonia.Windows;

namespace Kapok.View.Avalonia.Dock;

/// <summary>
/// Port of Kapok.View.Wpf.AvalonDock's PopupListPageWindow - confirmed a real consumer need, not
/// dead scaffolding: DuckAccounting.View.Wpf registers it explicitly (via
/// WpfViewDomain.RegisterPageWpfWindowConstructor, not through DefaultPopupListPageWindow, which
/// nothing actually reads) for several lookup-style list pages (CustomerAccountEntryList,
/// JournalEntryList, LedgerAccountEntryList, etc.) - a lighter popup chrome than the full
/// DockPageWindow: WPF's version collapses its Ribbon and shows a flat WrapPanel of per-group
/// toolbars built from the page's Base menu instead, and defaults to a narrower width than the
/// full 1000-wide ListPageWindow (see the constructor's own comment on why this doesn't also port
/// WPF's SizeToContent="Width").
///
/// Built on <see cref="DockPageWindow"/> (not plain PageWindow) to match WPF's own choice of the
/// AvalonDock-flavored PopupListPageWindow specifically - DuckAccounting's registrations are all
/// list pages that may carry detail pages too, so this keeps the same document+detail docking
/// DockPageWindow already provides, just with different chrome around it.
/// </summary>
public class PopupListPageWindow : DockPageWindow
{
    private WrapPanel? _toolbarPanel;

    public PopupListPageWindow()
    {
        // WPF's version uses SizeToContent="Width" - not ported: confirmed empirically that
        // Dock.Avalonia's DockControl has no comparable "natural" content width the way AvalonDock's
        // LayoutDocumentPane does (a real headless run sized the window to 1920px wide against a
        // page with no meaningful column content to size from). A fixed, narrower-than-ListPageWindow
        // default width is the safer equivalent of "less heavy" here.
        MinWidth = 150;
        Width = 500;
        Height = 800;

        // RebuildRibbon() (PageWindow.cs) replaces the Ribbon instance on every DataContext change,
        // so collapsing it once in the constructor wouldn't survive that - this reacts to the
        // RibbonProperty itself instead of racing DataContextChanged subscriber order against
        // PageWindow's own handler (which runs after BuildPageContentArea's caller-side
        // subscriptions, since it is added last in PageWindow's constructor).
        this.GetObservable(RibbonWindow.RibbonProperty).Subscribe(ribbon =>
        {
            if (ribbon != null)
                ribbon.IsCollapsed = true;
        });
    }

    protected override Control BuildPageContentArea()
    {
        var dockControl = base.BuildPageContentArea();

        _toolbarPanel = new WrapPanel();

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_toolbarPanel, global::Avalonia.Controls.Dock.Top);
        layout.Children.Add(_toolbarPanel);
        layout.Children.Add(dockControl);

        DataContextChanged += (_, _) => RebuildToolbar();

        return layout;
    }

    /// <summary>
    /// One flat toolbar row per group in the page's Base menu's first tab - matches WPF's
    /// ItemsControl/WrapPanel/per-group ToolBar structure, reusing RibbonMenuBuilder.BuildLeaf for
    /// the actual per-item controls instead of a second implementation of action/toggle/
    /// table-data-button/dropdown resolution.
    /// </summary>
    private void RebuildToolbar()
    {
        if (_toolbarPanel == null)
            return;

        _toolbarPanel.Children.Clear();

        if (DataContext is not InteractivePage page)
            return;
        if (!page.Menu.TryGetValue(UIMenu.BaseMenuName, out var baseMenu) || baseMenu.MenuItems.Count == 0)
            return;

        foreach (var groupItem in baseMenu.MenuItems[0].SubMenuItems)
        {
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Margin = new global::Avalonia.Thickness(2)
            };
            toolbar.Bind(Control.IsVisibleProperty, new Binding(nameof(UIMenuItem.IsVisible)) { Source = groupItem });
            ToolTip.SetTip(toolbar, Caption(groupItem.Label));

            foreach (var leaf in groupItem.SubMenuItems)
            {
                var control = RibbonMenuBuilder.BuildLeaf(leaf);
                if (control != null)
                    toolbar.Children.Add(control);
            }

            _toolbarPanel.Children.Add(toolbar);
        }
    }

    private static string? Caption(Kapok.Caption caption)
        => (string?)new CaptionConverter().Convert(caption, typeof(string), null, CultureInfo.CurrentUICulture);
}
