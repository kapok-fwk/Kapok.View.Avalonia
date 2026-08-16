using System.Globalization;
using Kapok.Entity;
using Kapok.Report.Model;

namespace Kapok.View.Avalonia.Report;

/// <summary>
/// Direct port of Kapok.View.Wpf's ReportParameterViewModel - framework-agnostic (EditableEntityBase
/// is a plain Kapok.Entity type, no WPF API usage), so no adaptation was needed beyond the namespace.
/// Dropped WPF's ValidateProperty override: it called base.ValidateProperty and otherwise did
/// nothing (all three branches either returned immediately or contained only a commented-out
/// line) - not a behavior to preserve.
/// </summary>
public class ReportParameterViewModel : EditableEntityBase
{
    public ReportParameterViewModel(ReportParameter reportParameter)
    {
        ReportParameter = reportParameter;

        if (reportParameter.DefaultIterativeValues != null)
        {
            ProposalValues = new List<object>(reportParameter.DefaultIterativeValues);
        }
    }

    public ReportParameter ReportParameter { get; }

    public bool HasIterativeValues { get; set; }

    private object? _value;
    public object? Value
    {
        get => _value;
        set
        {
            if (SetValidateProperty(ref _value, value))
            {
                if (_value == null)
                {
                    _value = ReportParameter.DataType.GetTypeDefault();
                }
                else if (ReportParameter.DataType == typeof(string))
                {
                    return;
                }
                else if (_value is IConvertible convertible)
                {
                    _value = convertible.ToType(ReportParameter.DataType, CultureInfo.CurrentUICulture);
                }
            }
        }
    }

    public List<object>? ProposalValues { get; }
}
