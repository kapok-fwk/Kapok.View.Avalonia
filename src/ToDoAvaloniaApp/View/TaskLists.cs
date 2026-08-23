using Kapok.BusinessLayer;
using Kapok.Entity.Model;
using Kapok.View;
using ToDoAvaloniaApp.DataModel;
using Task = ToDoAvaloniaApp.DataModel.Task;

namespace ToDoAvaloniaApp.View;

public class TaskLists : ListPage<TaskList>
{
    public TaskLists(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        Title = "Task lists";

        ListViews.Add(new DataSetListView
        {
            Name = "Standard",
            Columns = new List<ColumnPropertyView>
            {
                // Phase 7 item 4: the drill-down column. A DrillDownDefinition makes
                // CustomDataGrid generate a DataGridHyperlinkCommandColumn - the name renders as a
                // link that opens the Tasks page filtered to this list, the same thing the
                // Ribbon's "Tasks" button does, but reachable straight from the row.
                new(nameof(TaskList.Name))
                {
                    DrillDownDefinition = new DrillDownDefinition<Task, TaskList>(typeof(Tasks),
                        (filter, taskList, _) => filter.AddPropertyFilter(nameof(Task.TaskListId), taskList.Id))
                },
                // Phase 8 item 5: the two image column kinds ([BinaryImage]/[InfoImages]) - see
                // TaskList.Icon/Badges. Neither is auto-generated from plain reflection the way
                // Name/IsArchived are (both need the real metadata-driven column path), so they
                // only render at all once listed here, same as the drill-down column above.
                new(nameof(TaskList.Icon)) { Width = 60 },
                new(nameof(TaskList.Badges)) { Width = 70 }
            }
        });

        OpenTasksAction = new UIOpenReferencedPageAction<TaskList>("OpenTasks", typeof(Tasks), ServiceProvider, DataSet,
            filter: (filter, taskList, _) =>
            {
                var filterSet = (IFilterSet<Task>)filter;
                filterSet.AddPropertyFilter(nameof(Task.TaskListId), taskList.Id);
            });

        // Real usage for Phase 5's Report/ item: opens a genuine MimeTypeReportPageWindow via
        // ViewDomain.OpenReportDialog, backed by a real registered report model + processor (see
        // Report/TaskListsReport.cs, Report/TaskListsReportProcessor.cs).
        ReportAction = new UIAction("Report", () => ViewDomain.OpenReportDialog(new Report.TaskListsReport(), null, this));
    }

    [MenuItem, System.ComponentModel.DataAnnotations.Display(Name = "Tasks")]
    public IDataSetSelectionAction<TaskList> OpenTasksAction { get; }

    [MenuItem, System.ComponentModel.DataAnnotations.Display(Name = "Report")]
    public IAction ReportAction { get; }
}
