using Kapok.View;
using Task = ToDoAvaloniaApp.DataModel.Task;

namespace ToDoAvaloniaApp.View;

public class Tasks : ListPage<Task>
{
    public Tasks(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        Title = "Tasks";

        ListViews.Add(new DataSetListView
        {
            Name = "Standard",
            Columns = new List<ColumnPropertyView>
            {
                new(nameof(Task.Name)),
                new(nameof(Task.EstimatedTime)),
            }
        });
        ListViews.Add(new DataSetListView
        {
            Name = "With due date",
            Columns = new List<ColumnPropertyView>
            {
                new(nameof(Task.Name)),
                new(nameof(Task.EstimatedTime)),
                new(nameof(Task.DueDate)),
            }
        });

        // Real usage for the Phase 5 LookupComboBox/CustomComboBox port (TaskCard/TaskCardView):
        // ListPage<TEntry>.CreateNewEntry() auto-opens OpenCardPageAction when set (see
        // ListPage.cs), so creating a new Task now opens a real TaskCard dialog with a working
        // TaskListId lookup, rather than TaskCard being reachable only from test code.
        OpenCardPageAction = new UIOpenReferencedCardPageAction<Task>("OpenCardPage", typeof(TaskCard), ServiceProvider, DataSet);
    }
}
