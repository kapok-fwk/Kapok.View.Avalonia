using System.Collections.ObjectModel;
using Kapok.Report;
using Kapok.View.Avalonia.Localization;

namespace Kapok.View.Avalonia.Report;

/// <summary>
/// Direct port of Kapok.View.Wpf's ReportPage&lt;TReportProcessor, TReportModel&gt; - the logic here
/// (ReportParameters loading, SaveAsReportExecution) is already framework-agnostic, routed entirely
/// through ViewDomain's abstract members (OpenSaveFileDialog/ShowInfoMessage/ShowErrorMessage),
/// confirmed by reading the WPF source: no WPF API usage anywhere in this class.
///
/// Dropped WPF's own `CancelAction` property redeclaration: it shadowed the perfectly good
/// DialogPage.CancelAction (already wired to Cancel() -> DialogResult=false; Close()) with a new,
/// never-assigned one (`{ get; set; }`, no constructor assignment) - a latent bug in the WPF
/// source, not a behavior worth reproducing. The inherited DialogPage.CancelAction is used as-is.
///
/// Its two user-facing messages (export-successful / IO-exception) are looked up via
/// <see cref="ResxManager"/> from a ported Report/Resources/DataTableReportViewModel.resx -
/// Phase 5's Infralution.Localization.Wpf item built that lookup infrastructure; this class is
/// one of the two real consumers (see ResxManager.cs's own doc comment for why a full
/// ResxExtension-style markup extension wasn't ported, just the plain string-lookup it actually
/// reduces to for every real usage in Kapok.View.Wpf).
/// </summary>
public abstract class ReportPage<TReportProcessor, TReportModel> : DialogPage
    where TReportProcessor : ReportProcessor<TReportModel>
    where TReportModel : Kapok.Report.Model.Report
{
    protected ReportPage(TReportModel model, TReportProcessor? processor, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        if (processor != null)
        {
            Processor = processor;
            if (processor.ReportModel == null || processor.ReportModel != model)
                processor.ReportModel = model;
        }
        ReportModel = model;

        ReportParameters = new Collection<ReportParameterViewModel>();
        LoadReportParameters();

        // UI
        Title = ReportModel.Caption?.LanguageOrDefault(ViewDomain.Culture) ?? ReportModel.Name;
    }

    private void LoadReportParameters()
    {
        ReportParameters.Clear();

        foreach (var parameter in ReportModel.Parameters)
        {
            if ((parameter.DefaultIterativeValues?.Count ?? 0) > 0)
            {
                ReportParameters.Add(new ReportParameterViewModel(parameter)
                {
                    Value = parameter.DefaultValue,
                    // TODO/NOTE: iterative values currently cannot be edited in view
                    HasIterativeValues = true
                });
            }
            else
            {
                ReportParameters.Add(new ReportParameterViewModel(parameter)
                {
                    Value = parameter.DefaultValue
                });
            }
        }
    }

    protected TReportModel ReportModel { get; }

    protected TReportProcessor? Processor { get; }

    public Collection<ReportParameterViewModel> ReportParameters { get; }

    public event EventHandler? ProcessingDone;

    protected void OnProcessingDone(object sender, EventArgs e)
    {
        ProcessingDone?.Invoke(sender, e);
    }

    protected void SaveAsReportExecution(string saveDialogTitle, string saveFileDialogFilter, Action<Stream> processToStreamProcedure)
    {
        string? fileName = ViewDomain.OpenSaveFileDialog(saveDialogTitle, saveFileDialogFilter);
        if (fileName == null)
        {
            return;
        }

        var iterationParameter = ReportParameters.FirstOrDefault(t => t.HasIterativeValues);

        try
        {
            if (iterationParameter != null)
            {
                foreach (string iterationValue in iterationParameter.ReportParameter.DefaultIterativeValues!.Select(d => d?.ToString() ?? string.Empty))
                {
                    // prepare processor
                    Processor!.ParameterValues = ReportParameters.Where(p => !p.HasIterativeValues).ToDictionary(p => p.ReportParameter.Name, p => p.Value)!;
                    Processor.ParameterValues.Add(iterationParameter.ReportParameter.Name, iterationValue);

                    var newFileName =
                        Path.Combine(
                            Path.GetDirectoryName(fileName) ?? string.Empty,
                            Path.GetFileNameWithoutExtension(fileName) + " " + iterationValue + Path.GetExtension(fileName)
                        );

                    // execute processor
                    using var sw = new StreamWriter(newFileName);
                    processToStreamProcedure(sw.BaseStream);
                }
            }
            else
            {
                // prepare processor
                Processor!.ParameterValues = ReportParameters.ToDictionary(p => p.ReportParameter.Name, p => p.Value)!;

                // execute processor
                using var sw = new StreamWriter(fileName);
                processToStreamProcedure(sw.BaseStream);
            }

            SendNotificationOnExportSuccessful();
        }
        catch (IOException ex)
        {
            SendNotificationOnIOException(ex);
        }

        OnProcessingDone(this, EventArgs.Empty);
        Close();
    }

    private const string ResxName = "Kapok.View.Avalonia.Report.Resources.DataTableReportViewModel";

    protected void SendNotificationOnExportSuccessful()
    {
        ViewDomain.ShowInfoMessage(
            ResxManager.GetString(ResxName, "SendNotificationOnExportSuccessful_Message"),
            ResxManager.GetString(ResxName, "SendNotificationOnExportSuccessful_Caption"),
            this);
    }

    protected void SendNotificationOnIOException(IOException exception)
    {
        ViewDomain.ShowErrorMessage(
            ResxManager.GetString(ResxName, "SendNotificationOnIOException_Message"),
            ResxManager.GetString(ResxName, "SendNotificationOnIOException_Caption"),
            this, exception);
    }
}
