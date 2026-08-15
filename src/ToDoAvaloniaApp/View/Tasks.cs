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
    }
}
