using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes.Fluent;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Kapok.View.Avalonia.Windows;

namespace Kapok.View.Avalonia.Dock;

/// <summary>
/// Dock-flavored card/list page window - mirrors Kapok.View.Wpf.AvalonDock's ListPageWindow.xaml/
/// CardPageWindow.xaml (RibbonWindow + Ribbon, same as core PageWindow, but a DockingManager
/// hosting the page itself as a single document plus its DetailPages as tool panes, instead of a
/// plain ContentControl). Reuses all of PageWindow's Ribbon/KeyBindings/OK-button wiring via
/// BuildPageContentArea, matching WPF's SingleDataPageAsDocumentSourceConverter (one fixed
/// document, not a real multi-document create/close workflow - CanCreateDocument is off).
/// </summary>
public class DockPageWindow : PageWindow
{
    public Factory Factory { get; } = new();

    private IDocument? _document;
    private IToolDock? _toolDock;

    protected override Control BuildPageContentArea()
    {
        // Like AvaloniaControls.Ribbon.Desktop (see PageWindow's Styles.Add comment),
        // Dock.Avalonia's control templates/themes need to be registered explicitly - without
        // this, DockControl/DocumentDock/ToolDock have no visual template and render blank
        // (confirmed empirically: a headless screenshot with real DockControl content came back
        // fully white below the Ribbon before this was added).
        Styles.Add(new DockFluentTheme());

        Factory
            .Document(out var document, d => d.WithId("Document").WithTitle(string.Empty).WithCanClose(false))
            .DocumentDock(out var documentDock, d => d
                .WithId("Documents")
                .WithIsCollapsable(false)
                .WithCanCreateDocument(false))
            .ToolDock(out var toolDock, Alignment.Right, t => t.WithId("DetailPages"))
            .ProportionalDockSplitter(out var splitter)
            .ProportionalDock(out var mainLayout, global::Dock.Model.Core.Orientation.Horizontal, d => d
                .Add(documentDock, splitter, toolDock))
            .RootDock(out var root, r => r
                .Add(mainLayout)
                .WithDefaultDockable(mainLayout)
                .WithActiveDockable(mainLayout));

        Factory.AddDockable(documentDock, document);
        Factory.InitLayout(root);

        _document = document;
        _toolDock = toolDock;

        var dockControl = new global::Dock.Avalonia.Controls.DockControl
        {
            Factory = Factory,
            Layout = root
        };
        // AutoCreateDataTemplates (default true) is needed for Dock's own structural chrome
        // (RootDock/ProportionalDock/DocumentDock tab strips etc.) - confirmed empirically:
        // setting it false to avoid the default Document/Tool template (which presents the
        // dockable itself, not its Context - see DockableContentTemplate's doc comment) broke
        // that chrome too, rendering raw type names as text instead. Adding this template
        // explicitly is enough on its own - it's checked before Dock's built-in fallback for the
        // one case (a dockable whose Context is an IPage) it actually needs to override.
        dockControl.DataTemplates.Add(new DockableContentTemplate());

        DataContextChanged += (_, _) => OnDockDataContextSet();

        return dockControl;
    }

    private INotifyCollectionChanged? _subscribedDetailPages;

    private void OnDockDataContextSet()
    {
        if (DataContext is not IPage page || _document == null)
            return;

        _document.Title = page.Title;
        _document.Context = page;

        // DetailPages isn't exercised by any ToDoAvaloniaApp page today (see AvaloniaDockViewDomain's
        // FocusDocumentPage comment for the same caveat on DocumentPageCollectionPage) - wired for
        // correctness/parity with WPF's AnchorablesSource="{Binding DetailPages}", not live-verified.
        if (DataContext is InteractivePage interactivePage && _toolDock != null)
        {
            foreach (var detailPage in interactivePage.DetailPages)
                AddDetailPageTool(detailPage);

            _subscribedDetailPages = interactivePage.DetailPages;
            interactivePage.DetailPages.CollectionChanged += DetailPages_CollectionChanged;
        }
    }

    private void DetailPages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_toolDock == null)
            return;

        if (e.NewItems != null)
            foreach (IDetailPage detailPage in e.NewItems)
                AddDetailPageTool(detailPage);

        if (e.OldItems != null)
            foreach (IDetailPage detailPage in e.OldItems)
            {
                var tool = _toolDock.VisibleDockables?.FirstOrDefault(d => ReferenceEquals(d.Context, detailPage));
                if (tool != null)
                    Factory.RemoveDockable(tool, false);
            }
    }

    private void AddDetailPageTool(IDetailPage detailPage)
    {
        if (_toolDock == null)
            return;

        var tool = new Tool
        {
            Id = detailPage.GetHashCode().ToString(),
            Title = detailPage.Title,
            Context = detailPage,
            CanClose = detailPage.CanClose
        };
        Factory.AddDockable(_toolDock, tool);
    }
}
