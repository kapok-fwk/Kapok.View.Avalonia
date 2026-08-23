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
///
/// Also the real usage for Phase 5's UIElementDropBehavior: dropping a file onto the card
/// (TaskCardView wires the behavior onto its root Grid) records the file name(s) in the Task's
/// Description - a minimal but genuine effect, not just a no-op proving the event fired.
/// </summary>
public class TaskCard : CardPage<Task>, IDropTargetOnPage
{
    public TaskCard(IServiceProvider serviceProvider, IDataSetView<Task> tableData)
        : base(serviceProvider, tableData)
    {
        Title = "Task";

        // Shared with the Tasks list page's own lookup column - see TaskLookupDefinitions.
        PropertyViewDefinitions.Add(new PropertyView(nameof(Task.TaskListId))
        {
            LookupDefinition = TaskLookupDefinitions.TaskListLookup()
        });
    }

    public bool CanDropFile(string[] filenames) => filenames.Length > 0 && DataSet?.Current != null;

    public void DropFile(string[] filenames)
    {
        var fileNames = filenames.Select(System.IO.Path.GetFileName);
        DataSet!.Current!.Description = $"Attached: {string.Join(", ", fileNames)}";
    }
}
