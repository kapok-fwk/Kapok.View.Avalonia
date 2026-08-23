using Kapok.View;
using ToDoAvaloniaApp.DataModel;
using Task = ToDoAvaloniaApp.DataModel.Task;

namespace ToDoAvaloniaApp.View;

public class Tasks : ListPage<Task>
{
    public Tasks(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        Title = "Tasks";

        // Phase 7 item 1: these column definitions are what CustomDataGrid.ColumnsSource actually
        // generates the grid's columns from. Deliberately exercises every ColumnPropertyView
        // option the generator understands - explicit Width, TextWrap, IsHidden, an enum property,
        // a numeric property (right-aligned), a [DataType(Date)] property (string format "d") and
        // a property carrying DisplayShortName/DisplayDescription (header caption vs. tooltip) -
        // rather than the minimal two-column list this page had while the grid was still on plain
        // AutoGenerateColumns.
        ListViews.Add(new DataSetListView
        {
            Name = "Standard",
            Columns = new List<ColumnPropertyView>
            {
                new(nameof(Task.Name)) { Width = 160 },
                new(nameof(Task.Priority)),
                new(nameof(Task.Description)) { Width = 200, TextWrap = true },
                new(nameof(Task.EstimatedTime)) { Width = 90 },
                new(nameof(Task.DueDate)) { Width = 110 },
                // Phase 7 item 4: the lookup column. Its LookupDefinition is what makes
                // CustomDataGrid generate a DataGridLookupComboBoxColumn instead of a plain text
                // column - the cell shows the referenced TaskList's Name rather than the raw
                // TaskListId Guid, and (when the page is editable) edits through a real
                // LookupComboBox whose dropdown is a TaskList grid.
                new(nameof(Task.TaskListId)) { Width = 120, LookupDefinition = TaskLookupDefinitions.TaskListLookup() },
                // Proves IsHidden really suppresses a column: the property is part of the list
                // view, but must not appear in the grid.
                new(nameof(Task.Id)) { IsHidden = true },
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

        // Real usage for the DataGridStyling row-colouring port: Kapok's EntryColoring event lets
        // business logic decide a row's colours. Urgent tasks get a red-tinted row, overdue ones an
        // amber one - the kind of rule this feature exists for, and the only way to verify that the
        // grid actually asks the DataSet per row.
        DataSet!.EntryColoring += (_, e) =>
        {
            if (e.Entity is not Task task)
                return;

            if (task.Priority == TaskPriority.Urgent)
                e.BackgroundColor = System.Drawing.Color.FromArgb(255, 255, 220, 220);
            else if (task.DueDate.HasValue && task.DueDate.Value.Date < DateTime.Today)
                e.BackgroundColor = System.Drawing.Color.FromArgb(255, 255, 244, 214);
        };
    }
}
