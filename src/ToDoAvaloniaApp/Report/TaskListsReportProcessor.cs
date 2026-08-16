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

        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("IsArchived", typeof(bool));

        using var scope = _dataDomain.CreateScope();
        var taskLists = scope.GetEntityService<ToDoAvaloniaApp.DataModel.TaskList>().AsQueryable();
        foreach (var taskList in taskLists)
        {
            if (!includeArchived && taskList.IsArchived)
                continue;

            table.Rows.Add(taskList.Name, taskList.IsArchived);
        }

        return table;
    }
}
