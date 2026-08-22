using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Styling;
using Kapok.BusinessLayer;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// The per-column filter input shown under a column's caption in the header.
///
/// Port of Kapok.View.Wpf's DataGridColumnFilter + DataGridColumnHeaderWithFilterStyle.xaml.
/// Note what WPF's version actually is, because the name is misleading: it is not a popup - it is
/// an always-present TextBox in the second row of a re-templated DataGridColumnHeader, shown or
/// hidden by <c>CustomDataGrid.IsFilterVisible</c> (which the page's
/// <c>DataSet.ToggleFilterVisibleAction</c> toggles). This port keeps that behaviour rather than
/// inventing a popup, so a Kapok app behaves the same on both UI stacks.
///
/// Structurally it is built differently, for a real reason: WPF had to replace the entire
/// DataGridColumnHeader ControlTemplate (re-declaring the header border, both resize grippers and
/// the sort indicator by hand) just to get a second row underneath the caption. Avalonia's
/// <c>DataGridColumn.HeaderTemplate</c> already renders arbitrary content inside the stock header,
/// so the filter is placed there instead - the native extensibility point, and it leaves the real
/// Fluent header theme (grippers, sort arrows, hover states) completely untouched. That is also
/// why this port needs no equivalent of DataGridColumnHeaderWithFilterStyle.xaml.
///
/// WPF also carried a FontSizeToHeightConverter to size the TextBox from the grid's FontSize;
/// Avalonia's TextBox sizes itself from its own font, so it is not needed.
/// </summary>
public class DataGridColumnFilter : UserControl
{
    private readonly TextBox _textBox;
    private DataGridColumnFilterViewModel? _columnFilter;

    public DataGridColumnFilter()
    {
        _textBox = new TextBox
        {
            VerticalAlignment = VerticalAlignment.Top,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 1),
            // The Fluent TextBox theme's own MinHeight (32) makes a very tall filter row; WPF kept
            // this compact by deriving the height from the grid's FontSize
            // (FontSizeToHeightConverter). A small explicit MinHeight is the same idea without the
            // converter. It must not be 0: with no minimum the TextBox measures to ~12px, the
            // column header stays at its own 32px minimum, and the filter row renders as an
            // invisible sliver (confirmed by dumping the realized header/filter bounds).
            MinHeight = 22,
            // The Fluent TextBox theme also carries a MinWidth (~64px). A narrow column's header
            // is narrower than that, and the oversized box was then centred and clipped, so its
            // left/right borders disappeared and the filter input looked like two stray lines -
            // found by dumping the realized bounds (the "Est. h" column measured -10,0,64,22
            // inside a 45px header). Nothing here needs a minimum width.
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontWeight = FontWeight.Normal
        };

        // WPF applied both of these through an EventTrigger/InvokeCommandAction pair in its
        // ControlTemplate, purely because a Style Setter cannot call a method. Plain event
        // handlers do the same thing here.
        _textBox.LostFocus += (_, _) => ApplyFilter();
        _textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplyFilter();
                e.Handled = true;
            }
        };

        // Avalonia's Fluent TextBox theme renders binding validation errors *inline*, underneath
        // the box (its DataValidationErrors part), and the errors here are multi-line parser
        // messages - confirmed from a screenshot, the column header grew to roughly four times its
        // height for a single typo. WPF surfaced the same message as a tooltip instead, which is
        // both the parity target and the only thing that keeps the header row a fixed height, so
        // the inline presenter is templated away and the tooltip is filled in below. The red
        // ":error" border the theme also applies is kept - that part is exactly right, and is why
        // this control sets no border of its own.
        Styles.Add(new Style(x => x.OfType<DataValidationErrors>())
        {
            Setters =
            {
                new Setter(DataValidationErrors.ErrorTemplateProperty,
                    new FuncDataTemplate<object?>((_, _) => new Panel(), supportsRecycling: true))
            }
        });

        // DataValidationErrors.HasErrors is the native signal that the Text binding's source
        // reported validation errors (DataGridColumnFilterViewModel implements
        // INotifyDataErrorInfo, forwarding the filter object's own parse errors).
        _textBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == DataValidationErrors.HasErrorsProperty)
                UpdateErrorTooltip();
        };

        Content = _textBox;
        Focusable = false;
        Margin = new Thickness(0, 2, 0, 0);
    }

    private bool _canUserFilter = true;

    /// <summary>
    /// Whether this column can be filtered at all (mirrors
    /// <see cref="DataGridColumnExtensions.CanUserFilterProperty"/>, which the column generator
    /// sets from <see cref="ColumnPropertyView.IsFilterable"/>).
    ///
    /// A non-filterable column keeps the input in the tree but fully transparent and inert,
    /// rather than removing it: the filter row has to stay the same height across all columns or
    /// the header captions stop lining up. WPF solved the same problem with an explicitly sized
    /// "PART_GridPlaceholder" swapped in by a template trigger.
    /// </summary>
    public bool CanUserFilter
    {
        get => _canUserFilter;
        set
        {
            _canUserFilter = value;
            _textBox.Opacity = value ? 1 : 0;
            _textBox.IsHitTestVisible = value;
            _textBox.Focusable = value;
            _textBox.IsTabStop = value;
        }
    }

    /// <summary>
    /// The column's filter view model. Assigning it wires the TextBox to
    /// <see cref="DataGridColumnFilterViewModel.QueryString"/>/<see cref="DataGridColumnFilterViewModel.IsReadOnly"/>.
    /// </summary>
    public DataGridColumnFilterViewModel? ColumnFilter
    {
        get => _columnFilter;
        set
        {
            if (ReferenceEquals(_columnFilter, value))
                return;

            _columnFilter = value;

            if (value == null)
                return;

            _textBox.Bind(TextBox.TextProperty, new Binding(nameof(DataGridColumnFilterViewModel.QueryString))
            {
                Source = value,
                Mode = BindingMode.TwoWay,
                // PropertyChanged, matching WPF's UpdateSourceTrigger=PropertyChanged: the query
                // string has to be current on the view model when LostFocus/Enter fires
                // ApplyFilter, not one keystroke behind.
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            _textBox.Bind(TextBox.IsReadOnlyProperty, new Binding(nameof(DataGridColumnFilterViewModel.IsReadOnly))
            {
                Source = value,
                Mode = BindingMode.OneWay
            });

            UpdateErrorTooltip();
        }
    }

    /// <summary>
    /// Applies what the user typed. Public so a host (or a verification script) can drive the same
    /// path a LostFocus/Enter would.
    /// </summary>
    public void ApplyFilter()
    {
        ColumnFilter?.UpdateFilter();
        UpdateErrorTooltip();
    }

    /// <summary>
    /// Surfaces the filter-expression parse errors as the input's tooltip. WPF did this with a
    /// Validation.HasError trigger reading <c>(Validation.Errors)[0].ErrorContent</c> - and left a
    /// TODO next to it that it only shows the first error. Reading the whole error list off
    /// Avalonia's own <see cref="DataValidationErrors"/> closes that TODO for free.
    /// </summary>
    private void UpdateErrorTooltip()
    {
        var errors = DataValidationErrors.GetErrors(_textBox)?
            .Select(error => (error as BusinessLayerMessage)?.Text ?? error?.ToString())
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList();

        ToolTip.SetTip(_textBox, errors is { Count: > 0 } ? string.Join(Environment.NewLine, errors) : null);
    }
}
