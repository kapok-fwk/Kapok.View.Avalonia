using Kapok.Data;
using Kapok.Entity.Model;
using Kapok.View;
using ToDoAvaloniaApp.DataModel;
using Task = ToDoAvaloniaApp.DataModel.Task;

namespace ToDoAvaloniaApp.View;

/// <summary>
/// A real ICardPage usage for the Phase 5 LookupComboBox port: edits a single Task, including its
/// TaskListId reference via a LookupComboBox showing TaskList entries (Name only, since only
/// TaskList.Name carries [LookupColumn] - see DataModel/TaskList.cs). Opened automatically by
/// Tasks.OpenCardPageAction whenever a new Task is created (see Tasks.cs), so there's a real path
/// to reach it, not just a page nothing ever shows.
/// </summary>
public class TaskCard : CardPage<Task>
{
    public TaskCard(IServiceProvider serviceProvider, IDataSetView<Task> tableData)
        : base(serviceProvider, tableData)
    {
        Title = "Task";

        // The lookup entries don't depend on the current Task (any TaskList is a valid choice),
        // so the entry-independent LookupDefinition overload is used - matches
        // Kapok.Entity.Model.ILookupDefinition.EntriesFuncDependentOnEntry = false.
        PropertyViewDefinitions.Add(new PropertyView(nameof(Task.TaskListId))
        {
            LookupDefinition = new LookupDefinition<Task, TaskList, Guid?>(
                scope => scope.GetEntityService<TaskList>().AsQueryable().ToList(),
                taskList => taskList.Id)
        });
    }
}
