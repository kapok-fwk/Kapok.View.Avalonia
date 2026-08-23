using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes.Fluent;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Kapok.View.Avalonia.ValueConverter;
using Kapok.View.Avalonia.Windows;

namespace Kapok.View.Avalonia.Dock;

/// <summary>
/// Real multi-document docking host for a <see cref="DocumentPageCollectionPage"/> - the Avalonia
/// counterpart of what WPF apps hand-build in their own MainPageWindow.xaml
/// (ToDoWpfApp.ViewWpf.MainPageWindow, and DuckAccounting.View.Wpf.MainPageWindow, a second real
/// consumer of the exact same pattern), binding AvalonDock's DockingManager directly to
/// DocumentPages/DetailPages/CurrentDocumentPage via DocumentsSource/AnchorablesSource/ActiveContent
/// (the last through ActiveDocumentConverter).
///
/// Dock.Avalonia has no DocumentsSource-style auto-wrapping (see DockPageWindow's own doc comment
/// on this same gap for its one fixed document) - dockables are the model, not generated from an
/// arbitrary bound collection - so unlike WPF, where this behavior falls out of a XAML binding, an
/// actual class has to keep a Document dockable in sync with each entry of DocumentPages, and the
/// active dockable in sync with CurrentDocumentPage, both ways. Not built on DockPageWindow: that
/// class's single fixed, non-closable document doesn't apply here - this window's DataContext is
/// the DocumentPageCollectionPage itself (the page showing the collection), not one page to show.
/// </summary>
public class DocumentPageCollectionWindow : PageWindow
{
    /// <summary>
    /// Routes a document tab's built-in close button through IPage.CloseAction instead of
    /// FactoryBase's default (which would just remove the dockable directly) - so closing a
    /// document tab runs the page's own OnClosing/OnClosed logic and updates DocumentPages, the
    /// same as WPF's LayoutItemContainerStyle binding CloseCommand to Model.CloseAction. The
    /// resulting DocumentPages.Remove is what actually removes the tab, through
    /// DocumentPages_CollectionChanged - this method itself never removes anything.
    /// </summary>
    private sealed class HostFactory : Factory
    {
        public override void CloseDockable(IDockable dockable)
        {
            if (dockable is IDocument { Context: IPage page })
            {
                page.CloseAction.Execute();
                return;
            }

            base.CloseDockable(dockable);
        }
    }

    public Factory Factory { get; } = new HostFactory();

    private IDock? _documentDock;
    private IDock? _toolDock;
    private readonly Dictionary<IPage, IDocument> _documentByPage = new();
    private DocumentPageCollectionPage? _hostPage;
    private bool _syncingActiveDockable;

    protected override Control BuildPageContentArea()
    {
        // Same reasoning as DockPageWindow.BuildPageContentArea - Dock.Avalonia's control
        // templates/themes need to be registered explicitly.
        Styles.Add(new DockFluentTheme());

        Factory
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

        Factory.InitLayout(root);

        _documentDock = documentDock;
        _toolDock = toolDock;

        var dockControl = new global::Dock.Avalonia.Controls.DockControl
        {
            Factory = Factory,
            Layout = root
        };
        dockControl.DataTemplates.Add(new DockableContentTemplate());

        Factory.ActiveDockableChanged += Factory_ActiveDockableChanged;

        DataContextChanged += (_, _) => OnHostDataContextSet();
        DataContextChanged += (_, _) => AddDocumentCollectionKeyBindingsOnce();

        // Unlike PageWindow's own ContentControl+PageContentTemplate (whose Build() wires this - see
        // that class's own comment) and DockPageWindow's single fixed document (whose Context = page
        // makes DockableContentTemplate delegate to the same PageContentTemplate.Build()), this
        // window's own DataContext - the DocumentPageCollectionPage host itself - never becomes any
        // dockable's Context (only each of its DocumentPages entries does), so nothing would ever
        // run its OnLoadingAction/OnLoadedAction (Page.cs) without this - the exact gap
        // MimeTypeReportPageWindow's own doc comment already called out for the same reason ("this
        // window doesn't go through PageContentTemplate").
        //
        // Loaded only, not Initialized: confirmed empirically that Initialized never fires for a
        // Window built via a bare `new Window()` (no XAML loader ever calls ISupportInitialize.
        // EndInit() on it) - Page.OnLoadedInternal already tolerates this by calling
        // OnLoadingInternal() itself first when OnLoading hasn't run yet (see its own "makes sure
        // Loading is always called before Load" comment), so Loaded alone is enough to run both.
        Loaded += (_, _) =>
        {
            (DataContext as Page)?.OnLoadedAction.Execute();
            // Rebuild once more: OnLoading() (e.g. MainPage's own override) may have just replaced
            // Menu[Base].MenuItems - see RebuildRibbon's own comment on why the first build (done
            // eagerly from DataContextChanged, before OnLoadingAction/OnLoadedAction ever run) can
            // be stale here.
            RebuildRibbon();
        };

        return dockControl;
    }

    private bool _documentCollectionKeyBindingsAdded;

    /// <summary>
    /// DocumentPageCollectionPage exposes its own Current*-prefixed actions (CurrentDocumentSaveDataAction
    /// etc.) rather than the plain SaveDataAction/RefreshAction/... PageWindow's own
    /// AddDataPageKeyBindings binds - those simply don't resolve against a DocumentPageCollectionPage
    /// DataContext and stay inert, same tolerance that method's own doc comment already relies on for
    /// card pages. This adds the real set, matching WPF's MainPageWindow.xaml Window.InputBindings
    /// exactly (including its own Ctrl+W -&gt; CloseCurrentDocumentPageAction, which shadows the base
    /// class's Ctrl+W -&gt; CloseAction - both bindings fire, but CloseAction on a
    /// DocumentPageCollectionPage closes the whole window, which happens to be harmless here since
    /// nothing else in this app owns closing it).
    /// </summary>
    private void AddDocumentCollectionKeyBindingsOnce()
    {
        if (_documentCollectionKeyBindingsAdded || DataContext is not DocumentPageCollectionPage)
            return;
        _documentCollectionKeyBindingsAdded = true;

        void Bind(string gesture, string actionPath)
        {
            KeyBindings.Add(new KeyBinding
            {
                Gesture = KeyGesture.Parse(gesture),
                [!KeyBinding.CommandProperty] = new Binding(actionPath)
                {
                    Converter = new IActionToICommandConverter(),
                    Source = DataContext
                }
            });
        }

        Bind("Ctrl+W", nameof(DocumentPageCollectionPage.CloseCurrentDocumentPageAction));
        Bind("Ctrl+S", nameof(DocumentPageCollectionPage.CurrentDocumentSaveDataAction));
        Bind("F5", nameof(DocumentPageCollectionPage.CurrentDocumentRefreshAction));
        Bind("Ctrl+N", nameof(DocumentPageCollectionPage.CurrentDocumentCreateNewEntryAction));
        Bind("Ctrl+Delete", nameof(DocumentPageCollectionPage.CurrentDocumentDeleteEntryAction));
        Bind("F2", nameof(DocumentPageCollectionPage.CurrentDocumentEditEntryAction));
        Bind("Ctrl+Shift+F", nameof(DocumentPageCollectionPage.CurrentDocumentToggleFilterVisibleAction));
        Bind("Ctrl+E", nameof(DocumentPageCollectionPage.CurrentDocumentExportAsExcelSheetAction));
    }

    private void OnHostDataContextSet()
    {
        if (_hostPage != null)
        {
            _hostPage.DocumentPages.CollectionChanged -= DocumentPages_CollectionChanged;
            _hostPage.DetailPages.CollectionChanged -= DetailPages_CollectionChanged;
            _hostPage.PropertyChanged -= HostPage_PropertyChanged;
        }

        _documentByPage.Clear();
        _documentDock?.VisibleDockables?.Clear();
        _toolDock?.VisibleDockables?.Clear();

        if (DataContext is not DocumentPageCollectionPage hostPage || _documentDock == null)
        {
            _hostPage = null;
            return;
        }

        _hostPage = hostPage;

        foreach (var page in hostPage.DocumentPages)
            AddDocumentTab(page);
        hostPage.DocumentPages.CollectionChanged += DocumentPages_CollectionChanged;

        if (_toolDock != null)
        {
            foreach (var detailPage in hostPage.DetailPages)
                AddDetailPageTool(detailPage);
            hostPage.DetailPages.CollectionChanged += DetailPages_CollectionChanged;
        }

        hostPage.PropertyChanged += HostPage_PropertyChanged;
        SyncActiveDockableFromCurrentDocumentPage();
    }

    private void DocumentPages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (IPage page in e.NewItems)
                AddDocumentTab(page);

        if (e.OldItems != null)
            foreach (IPage page in e.OldItems)
                RemoveDocumentTab(page);
    }

    private void AddDocumentTab(IPage page)
    {
        if (_documentDock == null || _documentByPage.ContainsKey(page))
            return;

        // CanClose = true is safe here (unlike DockPageWindow's one fixed document, which keeps it
        // off) because HostFactory.CloseDockable above routes the resulting close through
        // IPage.CloseAction rather than letting FactoryBase remove the dockable directly.
        var document = new Document
        {
            Id = page.GetHashCode().ToString(),
            Title = page.Title,
            Context = page,
            CanClose = true
        };

        Factory.AddDockable(_documentDock, document);
        _documentByPage[page] = document;
    }

    private void RemoveDocumentTab(IPage page)
    {
        if (!_documentByPage.TryGetValue(page, out var document))
            return;

        Factory.RemoveDockable(document, false);
        _documentByPage.Remove(page);
    }

    // Mirrors DockPageWindow.DetailPages_CollectionChanged/AddDetailPageTool exactly - not shared
    // between the two classes, since DockPageWindow's version reacts to InteractivePage.DetailPages
    // directly while this one reacts to the DocumentPageCollectionPage host's own DetailPages
    // (populated from whichever document page is current - see DocumentPageCollectionPage.
    // ShowDocumentPageInternal/HideDocumentPageInternal), and the two classes have no shared base
    // that already owns _toolDock/Factory to hang a shared helper off of.
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

    private void HostPage_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentPageCollectionPage.CurrentDocumentPage))
            SyncActiveDockableFromCurrentDocumentPage();
    }

    /// <summary>
    /// CurrentDocumentPage -&gt; ActiveDockable. The other half of the two-way sync WPF got for free
    /// from ActiveContent="{Binding CurrentDocumentPage, Mode=TwoWay, Converter=...}".
    /// </summary>
    private void SyncActiveDockableFromCurrentDocumentPage()
    {
        if (_hostPage == null || _syncingActiveDockable)
            return;

        var page = _hostPage.CurrentDocumentPage;
        if (page == null || !_documentByPage.TryGetValue(page, out var document))
            return;

        _syncingActiveDockable = true;
        try
        {
            Factory.SetActiveDockable(document);
        }
        finally
        {
            _syncingActiveDockable = false;
        }
    }

    /// <summary>
    /// ActiveDockable -&gt; CurrentDocumentPage (e.g. the user clicked a different document tab).
    /// ActiveDockableChanged fires for any dockable in the whole factory becoming active, not just
    /// document tabs (e.g. a tool pane gaining focus), so this only reacts when the newly active
    /// dockable is actually one of ours.
    /// </summary>
    private void Factory_ActiveDockableChanged(object? sender, ActiveDockableChangedEventArgs e)
    {
        if (_hostPage == null || _syncingActiveDockable)
            return;

        var page = ActiveDocumentConverter.ToPage(e.Dockable);
        if (page == null || !_documentByPage.ContainsKey(page))
            return;

        _syncingActiveDockable = true;
        try
        {
            _hostPage.CurrentDocumentPage = page;
        }
        finally
        {
            _syncingActiveDockable = false;
        }
    }
}
