using Kapok.Report.Model;

namespace ToDoAvaloniaApp.Report;

/// <summary>
/// Real usage for Phase 5's Report/ item: a minimal, genuinely-registered report model for
/// TaskLists, exercising ViewDomain.OpenReportDialog end to end (MimeTypeReportPage/
/// MimeTypeReportPageWindow, mime-type selection, real Excel/CSV export) rather than leaving the
/// port unverified beyond "it compiles".
/// </summary>
public class TaskListsReport : DataTableReport
{
    public TaskListsReport()
    {
        Name = "TaskListsReport";
        Caption = "Task Lists Report";

        // One real parameter, to exercise ReportParameterList's checkbox editor path (not just
        // the always-taken text-box fallback).
        Parameters.Add(new ReportParameter(nameof(IncludeArchived), typeof(bool))
        {
            Caption = "Include archived lists",
            DefaultValue = false
        });

        Fields = new List<DataSetField>
        {
            new() { Name = "Name", Caption = "Name" },
            new() { Name = "IsArchived", Caption = "Archived" }
        };
    }

    // Referenced only via nameof() above for the parameter name - the actual value is read back
    // out of ReportProcessor.ParameterValues by name in TaskListsReportProcessor, matching how
    // every other report parameter is consumed (there's no strongly-typed parameter binding in
    // Kapok.Report).
    public bool IncludeArchived { get; set; }
}
