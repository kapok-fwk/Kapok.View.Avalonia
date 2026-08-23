using Kapok.Data;
using Kapok.Module;
using Kapok.Report;
using Kapok.View;
using Kapok.View.Avalonia;
using Kapok.View.Avalonia.Dock;
using Kapok.View.Avalonia.Windows;
using ToDoAvaloniaApp.BusinessLogic;
using ToDoAvaloniaApp.DataModel;
using ToDoAvaloniaApp.View;
using Task = ToDoAvaloniaApp.DataModel.Task;

namespace ToDoAvaloniaApp;

public class ToDoModule : ModuleBase
{
    public ToDoModule() : base(nameof(ToDoModule))
    {
        // data model registration
        DataDomain.RegisterEntity<Task, TaskService>();
        DataDomain.RegisterEntity<TaskList>();
        // Phase 7 item 4: the hierarchical showcase entity (see DataModel/TaskCategory.cs).
        DataDomain.RegisterEntity<TaskCategory>();

        // Phase 5 Report/ item's real usage: ReportModule registers Kapok.Report's own entities
        // (ReportModel/ReportLayout/ReportDesign/...) into the same DataDomain - DbContextBase
        // builds its EF model from DataDomain.DataEntities globally (confirmed by reading it), so
        // this composes with ToDoModule's own registrations above without any special wiring.
        // TaskListsReportProcessor is registered explicitly (not left to
        // DataTableReportProcessor<>'s own generic self-registration) so it - not the
        // unimplemented generic base (ProcessToDataTable() throws NotImplementedException,
        // confirmed by reading it) - is what ReportEngine picks for TaskListsReport.
        ModuleEngine.InitiateModule(typeof(ReportModule));
        ReportEngine.RegisterProcessor(typeof(Report.TaskListsReportProcessor), typeof(Report.TaskListsReport));

        // register default pages for data models
        ViewDomain.RegisterEntityDefaultPage<Task>(typeof(Tasks));
        ViewDomain.RegisterEntityDefaultPage<TaskList>(typeof(TaskLists));
        ViewDomain.RegisterEntityDefaultPage<TaskCategory>(typeof(TaskCategories));

        // MainPage is a plain InteractivePage (see View/MainPage.cs) - it doesn't match any of
        // AvaloniaViewDomain.ConstructWindow's IListPage/IDialogPage/ICardPage fallbacks, so it
        // needs an explicit window constructor, same as WpfViewDomain.RegisterPageWpfWindowConstructor
        // did for the WPF version's MainPageWindow. Reuses the generic PageWindow rather than a
        // dedicated MainPageWindow subclass - no custom chrome needed yet in this phase.
        AvaloniaViewDomain.RegisterPageWindowConstructor<MainPage>(() => new PageWindow());

        // Phase 8 item 7: TestPage (see View/TestPage.cs) is the exact same shape as MainPage - a
        // plain InteractivePage, not IListPage/IDialogPage/ICardPage - but never got the same
        // registration MainPage got above, so showing it threw NotSupportedException
        // ("No Avalonia window defined for page") since Phase 1. Confirmed by reading
        // AvaloniaViewDomain.ConstructWindow: it has no fallback at all for a page that isn't one
        // of those three, matching MainPage's own comment above almost exactly - this was a gap in
        // this module's own registration, not in AvaloniaViewDomain.
        //
        // DockPageWindow, not the plain PageWindow MainPage uses: TestPage is also this session's
        // real usage for InteractivePage.DetailPages -> ToolDock (see TestPage.cs and
        // DockPageWindow's own "not live-verified" comment) - that wiring only exists on
        // DockPageWindow, so TestPage needs to be hosted there to prove it for real.
        AvaloniaViewDomain.RegisterPageWindowConstructor<TestPage>(() => new DockPageWindow());

        // TaskCard (Phase 5's real LookupComboBox usage) needs its own hand-built control -
        // Kapok.View.Avalonia's generic CardPageView is a deliberate "not implemented" placeholder
        // (see its own doc comment), matching WPF's ICardPage story of every real card page
        // supplying its own control.
        AvaloniaViewDomain.RegisterPageControlType<TaskCard>(typeof(TaskCardView));
    }
}
