using Kapok.Data;
using Kapok.Module;
using Kapok.Report;
using Kapok.View;
using Kapok.View.Avalonia;
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

        // TaskCard (Phase 5's real LookupComboBox usage) needs its own hand-built control -
        // Kapok.View.Avalonia's generic CardPageView is a deliberate "not implemented" placeholder
        // (see its own doc comment), matching WPF's ICardPage story of every real card page
        // supplying its own control.
        AvaloniaViewDomain.RegisterPageControlType<TaskCard>(typeof(TaskCardView));
    }
}
