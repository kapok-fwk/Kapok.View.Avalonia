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

        // Phase 8 item 7: exercises ReportParameterList's ComboBox editor branch
        // (ReportParameterViewModel.ProposalValues, sourced from DefaultIterativeValues) - the
        // only other real parameter in this app before this was a plain bool, which never touched
        // that branch. Genuinely drives the query's sort order (see TaskListsReportProcessor),
        // not just accepted and ignored.
        Parameters.Add(new ReportParameter(nameof(SortBy), typeof(string))
        {
            Caption = "Sort by",
            DefaultValue = "Name",
            DefaultIterativeValues = new List<object> { "Name", "IsArchived" }
        });

        // Exercises ReportParameterList's DatePicker editor branch (ReportParameter.DataType ==
        // typeof(DateTime)) - TaskList has no creation-date field to meaningfully filter by, so
        // this is accepted and round-tripped (visible in the exported report's own text, see
        // TaskListsReportProcessor) rather than driving a query filter - still real proof the
        // DatePicker binding actually reads/writes ReportParameterViewModel.Value, which is what
        // this item needs to verify.
        Parameters.Add(new ReportParameter(nameof(GeneratedOn), typeof(DateTime))
        {
            Caption = "Generated on",
            DefaultValue = DateTime.Today
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
    public string SortBy { get; set; } = "Name";
    public DateTime GeneratedOn { get; set; }
}
