using System.ComponentModel.DataAnnotations;
using Kapok.View;

namespace ToDoAvaloniaApp.View;

/// <summary>
/// Real DocumentPageCollectionPage, matching WPF's ToDoWpfApp.View.MainPage - superseding this
/// class's earlier "Phase 1 simplification" as a plain InteractivePage, now that
/// Kapok.View.Avalonia.Dock's DocumentPageCollectionWindow gives it a real multi-document host to
/// open into (each OpenXxxAction below routes here automatically once PatchMenuToOpenHere runs -
/// see DocumentPageCollectionPage.ShowDocumentPage - because HostPage is left unset only when no
/// DocumentPageCollectionPage exists to receive it; UIOpenPageAction.OpenPage() then falls back to
/// page.Show(), the plain top-level-window behavior Phase 1 was proving).
///
/// Unlike WPF's version, this does not call an AvaloniaDockViewDomain.FocusDocumentPage-style
/// method from OnSelectedDocumentPageChanged: DocumentPageCollectionWindow already keeps the active
/// document tab in sync reactively (see its HostPage_PropertyChanged/
/// SyncActiveDockableFromCurrentDocumentPage), because AddDocumentTab always runs synchronously
/// before CurrentDocumentPage is set (ShowDocumentPage adds to DocumentPages first) - there is no
/// AvalonDock DocumentsSource-style lazy-wrapping timing gap here to work around.
/// </summary>
public class MainPage : DocumentPageCollectionPage
{
    public MainPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        Title = "Simple ToDo Application";

        // this is the menu for the navigation bar
        AddMenu("Main");

        OpenToDoListAction = new UIOpenPageAction("OpenToDoList", typeof(TaskLists), ServiceProvider);
        OpenToDosAction = new UIOpenPageAction("OpenToDos", typeof(Tasks), ServiceProvider);
        OpenTestPageAction = new UIOpenPageAction("OpenTestPage", typeof(TestPage), ServiceProvider);
    }

    protected override void OnLoading()
    {
        base.OnLoading();

        // Set name of ribbon menu which is visible when no current page is selected.
        Menu[UIMenu.BaseMenuName].MenuItems[0] = new UIMenuItemTab("App")
        {
            Label = "App",
            Description = "Application",
            IsVisible = true,
            RibbonKeyTip = "A"
        };
    }

    protected override void OnSelectedDocumentPageChanged(IPage? oldPage, IPage? page)
    {
        base.OnSelectedDocumentPageChanged(oldPage, page);

        // hide/make visible the ribbon menu which is dependent on the current page
        Menu[UIMenu.BaseMenuName].MenuItems[0].IsVisible = page == null;
        if (page == null && Menu[UIMenu.BaseMenuName].MenuItems[0] is UIMenuItemTab tabItem)
        {
            tabItem.IsSelected = true;
        }
    }

    [MenuItem(MenuName = "Main"), Display(Name = "Open ToDo List")]
    public IAction OpenToDoListAction { get; }

    [MenuItem(MenuName = "Main"), Display(Name = "Open ToDos")]
    public IAction OpenToDosAction { get; }

    [MenuItem(MenuName = "Main"), Display(Name = "Test page")]
    public IAction OpenTestPageAction { get; }

    protected override void OnClosed()
    {
        base.OnClosed();

        ViewDomain.ShutdownApplication?.Invoke(0);
    }
}
