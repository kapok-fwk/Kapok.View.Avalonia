using System.Collections.ObjectModel;
using Kapok.Data;
using Kapok.Report;
using Kapok.Report.DataModel;
using Microsoft.Extensions.DependencyInjection;

namespace Kapok.View.Avalonia.Report;

/// <summary>
/// Port of Kapok.View.Wpf's MimeTypeReportPage - same framework-agnostic logic (routes entirely
/// through ReportEngine/ViewDomain), self-registers its window the same way (a static constructor
/// calling RegisterPageWindowConstructor, the Avalonia equivalent of WPF's
/// RegisterPageWpfWindowConstructor) so any consumer of ViewDomain.OpenReportDialog gets a working
/// window without needing its own explicit registration.
///
/// WPF's version hard-codes a 20-entry mime-type-to-caption dictionary (bmp/docx/html/jpeg/json/
/// emf/mhtml/pdf/png/pptx/rtf/svg/txt/tiff/xhtml/xls/xlsx/xml/xps) plus a separate call to a
/// `MimeTypeMap.GetExtension` helper (from the *published* Kapok.Report NuGet package WPF
/// references - not present in the local kapok-fwk checkout this project project-references,
/// confirmed by checking Kapok.Report.csproj's own package list). Rather than adding a new NuGet
/// dependency just to look up file extensions for 20 mostly-unreachable mime types, this keeps
/// one small (mimeType -> caption, extension) table scoped to what a real registered report
/// processor in this port can actually produce - confirmed by reading
/// DataTableReportProcessor.SupportedMimeTypes, the only IMimeTypeReportProcessor this port
/// registers (see ToDoAvaloniaApp's TaskListsReportProcessor): Excel 2007+, Excel 97-2003, and
/// CSV. If a future report processor supports more mime types, add their entries then.
/// </summary>
public sealed class MimeTypeReportPage : ReportPage<ReportProcessor<Kapok.Report.Model.Report>, Kapok.Report.Model.Report>
{
    private MimeTypeViewModel? _selectedMimeType;
    private readonly ReportEngine _reportEngine;

    private const string MimeTypeExcel2007 = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string MimeTypeExcel2003 = "application/vnd.ms-excel";
    private const string MimeTypeCsv = "text/csv";

    private static readonly IReadOnlyDictionary<string, (string DisplayName, string FileExtension)> MimeTypeInfo =
        new Dictionary<string, (string, string)>
        {
            { MimeTypeExcel2007, ("Microsoft Excel 2007+ (*.xlsx)", ".xlsx") },
            { MimeTypeExcel2003, ("Microsoft Excel 97-2003 (*.xls)", ".xls") },
            { MimeTypeCsv, ("CSV file (*.csv)", ".csv") }
        };

    static MimeTypeReportPage()
    {
        AvaloniaViewDomain.RegisterPageWindowConstructor<MimeTypeReportPage>(() => new MimeTypeReportPageWindow());
    }

    public MimeTypeReportPage(Kapok.Report.Model.Report model, IServiceProvider serviceProvider, ReportLayout? layout = null)
        : base(model, null, serviceProvider)
    {
        _reportEngine = new ReportEngine(serviceProvider.GetRequiredService<IDataDomain>());
        ReportLayout = _reportEngine.GetOrCreateReportLayout(model, null);

        IsDesignable = _reportEngine.IsModelDesignable(model);
        var supportedMimeTypes = _reportEngine.GetSupportedMimeTypes(model, layout);

        // UI
        SaveAsFileAction = new UIAction("SaveAsFile", Save, CanSave);
        DesignAction = new UIAction("Design", Design, CanDesign);

        SupportedMimeTypes = new ObservableCollection<MimeTypeViewModel>();

        SupportedMimeTypes.AddRange(
            from mimeType in supportedMimeTypes
            where MimeTypeInfo.ContainsKey(mimeType) // hide mime types with no known display/extension entry
            let info = MimeTypeInfo[mimeType]
            orderby info.DisplayName
            select new MimeTypeViewModel
            {
                MimeType = mimeType,
                FileExtension = info.FileExtension,
                DisplayName = info.DisplayName
            }
        );

        if (SupportedMimeTypes.Count == 1)
            SelectedMimeType = SupportedMimeTypes.First();
        else
        {
            var defaultMimeTypesOrder = new[] { MimeTypeExcel2007, MimeTypeExcel2003, MimeTypeCsv };

            foreach (var defaultMimeType in defaultMimeTypesOrder)
            {
                var mimeType = SupportedMimeTypes.FirstOrDefault(m => m.MimeType == defaultMimeType);
                if (mimeType != null)
                {
                    SelectedMimeType = mimeType;
                    break;
                }
            }
        }
    }

    public ReportLayout ReportLayout { get; set; }

    public bool ShowReportParameters => ReportParameters.Count > 0;

    public bool IsDesignable { get; }

    public MimeTypeViewModel? SelectedMimeType
    {
        get => _selectedMimeType;
        set => SetProperty(ref _selectedMimeType, value);
    }

    public ObservableCollection<MimeTypeViewModel> SupportedMimeTypes { get; }

    public IAction SaveAsFileAction { get; }

    public IAction DesignAction { get; }

    public class MimeTypeViewModel
    {
        public string? MimeType { get; set; }
        public string? FileExtension { get; set; }
        public string? DisplayName { get; set; }
    }

    private void Save()
    {
        string? fileName = ViewDomain.OpenSaveFileDialog(
            "Save report as",
            $"{SelectedMimeType!.DisplayName} (*{SelectedMimeType.FileExtension})|*{SelectedMimeType.FileExtension}");
        if (fileName == null)
        {
            return;
        }

        try
        {
            using (var sw = new StreamWriter(fileName))
            {
                _reportEngine.ExecuteReport(
                    ReportModel,
                    ReportParameters.ToDictionary(p => p.ReportParameter.Name, p => p.Value)!,
                    SelectedMimeType.MimeType!,
                    sw.BaseStream,
                    ReportLayout);
            }

            if (ViewDomain.ShowConfirmMessage(
                    "The report was exported successfully. Do you want to open it now?",
                    "Export successful",
                    this))
            {
                ViewDomain.OpenFile(fileName);
            }
        }
        catch (IOException exception)
        {
            ViewDomain.ShowErrorMessage(
                "The report could not be saved to the selected file.",
                "Export failed",
                this,
                exception);
        }
        catch (Exception exception)
        {
            ViewDomain.ShowErrorMessage(
                "An error occurred while executing the report.",
                "Report execution failed",
                this,
                exception);
        }

        OnProcessingDone(this, EventArgs.Empty);
        DialogResult = true;
        Close();
    }

    private bool CanSave()
    {
        return SelectedMimeType != null;
    }

    private void Design()
    {
        try
        {
            _reportEngine.OpenDesignDialog(
                ReportModel,
                ViewDomain,
                ReportLayout);
        }
        catch (Exception exception)
        {
            ViewDomain.ShowErrorMessage(
                "An error occurred while opening the report design dialog.",
                "Report design failed",
                this,
                exception);
        }
    }

    private bool CanDesign()
    {
        return IsDesignable;
    }
}
