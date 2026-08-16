using Kapok.BusinessLayer;
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
                new(nameof(TaskList.Name))
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
