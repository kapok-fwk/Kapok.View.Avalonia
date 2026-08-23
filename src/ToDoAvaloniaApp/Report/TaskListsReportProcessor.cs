using System.Data;
using Kapok.Data;
using Kapok.Report;

namespace ToDoAvaloniaApp.Report;

/// <summary>
/// DataTableReportProcessor's own ProcessToDataTable() is unimplemented (throws
/// NotImplementedException - confirmed by reading it, not assumed) - every real report needs its
/// own override providing the actual data. This one queries TaskList directly via IDataDomain,
/// same pattern TaskCard's LookupDefinition already uses elsewhere in this port.
/// </summary>
public class TaskListsReportProcessor : DataTableReportProcessor<TaskListsReport>
{
    private readonly IDataDomain _dataDomain;

    public TaskListsReportProcessor(IDataDomain dataDomain)
    {
        _dataDomain = dataDomain;
    }

    public override DataTable ProcessToDataTable()
    {
        ValidateRequiredFields();
        ValidateReportModel();

        var includeArchived = ParameterValues.TryGetValue(nameof(TaskListsReport.IncludeArchived), out var value)
            && value is true;

        // Phase 8 item 7: real usage for ReportParameterList's ComboBox editor - genuinely
        // reorders the exported rows rather than accepting and ignoring the selection.
        var sortBy = ParameterValues.TryGetValue(nameof(TaskListsReport.SortBy), out var sortByValue)
            ? sortByValue as string
            : null;

        // Real usage for the DatePicker editor - echoed into the exported table's own generated-on
        // column (TaskList has no creation-date field to filter by instead).
        var generatedOn = ParameterValues.TryGetValue(nameof(TaskListsReport.GeneratedOn), out var generatedOnValue)
            && generatedOnValue is DateTime dateTimeValue
                ? dateTimeValue
                : DateTime.Today;

        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("IsArchived", typeof(bool));
        table.Columns.Add("GeneratedOn", typeof(DateTime));

        using var scope = _dataDomain.CreateScope();
        var taskLists = scope.GetEntityService<ToDoAvaloniaApp.DataModel.TaskList>().AsQueryable();

        IEnumerable<ToDoAvaloniaApp.DataModel.TaskList> sortedTaskLists = sortBy == "IsArchived"
            ? taskLists.OrderBy(t => t.IsArchived).ThenBy(t => t.Name)
            : taskLists.OrderBy(t => t.Name);

        foreach (var taskList in sortedTaskLists)
        {
            if (!includeArchived && taskList.IsArchived)
                continue;

            table.Rows.Add(taskList.Name, taskList.IsArchived, generatedOn);
        }

        return table;
    }
}
