using Avalonia;
using Avalonia.Controls;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// Attached properties carried on a generated <see cref="DataGridColumn"/>. Direct port of
/// Kapok.View.Wpf's DataGridColumnExtensions - the concept (park Kapok-specific per-column state
/// on the column object itself, so the header template / filter UI can read it back later) maps
/// 1:1; only the registration API differs (Avalonia's AvaloniaProperty.RegisterAttached instead of
/// WPF's DependencyProperty.RegisterAttached).
///
/// Note that WPF registered <c>HeaderTooltip</c>/<c>ColumnViewModel</c> with
/// <c>DependencyProperty.Register</c> (not <c>RegisterAttached</c>) and then used them as if they
/// were attached - which happens to work in WPF but is not how the API is meant to be used. Both
/// are registered properly as attached properties here.
/// </summary>
public static class DataGridColumnExtensions
{
    /// <summary>
    /// Whether the per-column filter UI is offered for this column. Set to <c>false</c> for
    /// columns whose <see cref="ColumnPropertyView.IsFilterable"/> is <c>false</c>
    /// (e.g. [NotMapped] / [InfoImages] properties, which have no queryable database column).
    /// </summary>
    public static readonly AttachedProperty<bool> CanUserFilterProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, bool>(
            "CanUserFilter", typeof(DataGridColumnExtensions), defaultValue: true);

    public static bool GetCanUserFilter(DataGridColumn target) => target.GetValue(CanUserFilterProperty);

    public static void SetCanUserFilter(DataGridColumn target, bool value) => target.SetValue(CanUserFilterProperty, value);

    /// <summary>
    /// The tooltip content shown on the column header (built from the column's DisplayName /
    /// DisplayDescription captions).
    /// </summary>
    public static readonly AttachedProperty<object?> HeaderTooltipProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, object?>(
            "HeaderTooltip", typeof(DataGridColumnExtensions));

    public static object? GetHeaderTooltip(DataGridColumn target) => target.GetValue(HeaderTooltipProperty);

    public static void SetHeaderTooltip(DataGridColumn target, object? value) => target.SetValue(HeaderTooltipProperty, value);

    /// <summary>
    /// The <see cref="ColumnPropertyView"/> this column was generated from. Used to find a column
    /// back from its metadata object when <c>ColumnsSource</c> changes, and by the filter UI to
    /// know which property it filters on.
    /// </summary>
    public static readonly AttachedProperty<object?> ColumnViewModelProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, object?>(
            "ColumnViewModel", typeof(DataGridColumnExtensions));

    public static object? GetColumnViewModel(DataGridColumn target) => target.GetValue(ColumnViewModelProperty);

    public static void SetColumnViewModel(DataGridColumn target, object? value) => target.SetValue(ColumnViewModelProperty, value);
}
