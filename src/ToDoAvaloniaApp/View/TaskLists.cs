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
    }

    [MenuItem, System.ComponentModel.DataAnnotations.Display(Name = "Tasks")]
    public IDataSetSelectionAction<TaskList> OpenTasksAction { get; }
}
