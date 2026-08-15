using System.ComponentModel.DataAnnotations;
using Kapok.View;

namespace ToDoAvaloniaApp.View;

/// <summary>
/// Phase 1 simplification: a plain InteractivePage rather than WPF's DocumentPageCollectionPage.
/// DocumentPageCollectionPage (Kapok's MDI/tab-host page type) is only really meaningful once a
/// docking host exists to render it - Kapok.View.Wpf.AvalonDock's WpfAvalonDockViewDomain is what
/// gives it real behavior (FocusDocumentPage etc.) in the WPF module too. Here, with HostPage left
/// unset on each UIOpenPageAction, UIOpenPageAction.OpenPage() falls back to page.Show() -
/// exactly the plain top-level window behavior this phase is meant to prove. Revisit as a real
/// DocumentPageCollectionPage once Phase 3 (Dock integration) lands.
/// </summary>
public class MainPage : InteractivePage
{
    public MainPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        Title = "Simple ToDo Application";

        AddMenu("Main");

        OpenToDoListAction = new UIOpenPageAction("OpenToDoList", typeof(TaskLists), ServiceProvider);
        OpenToDosAction = new UIOpenPageAction("OpenToDos", typeof(Tasks), ServiceProvider);
        OpenTestPageAction = new UIOpenPageAction("OpenTestPage", typeof(TestPage), ServiceProvider);
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
