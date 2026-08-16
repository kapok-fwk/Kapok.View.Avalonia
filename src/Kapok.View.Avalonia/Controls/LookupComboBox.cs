using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.VisualTree;
using Kapok.Entity;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// Direct-intent port of Kapok.View.Wpf's LookupComboBox: a combo box whose dropdown is a
/// read-only, single-selection data grid instead of a plain item list, so a lookup can show more
/// than one column (e.g. a code + a description) while still behaving like a combo box.
///
/// WPF's version finds its own template's "DropDownScrollViewer" part at OnApplyTemplate time and
/// swaps that part's parent Decorator.Child for a DataGrid it builds by hand. Avalonia's Fluent
/// ComboBox template (checked against the real theme XAML in the Avalonia repo) has no equivalent
/// named part - the popup's ScrollViewer/ItemsPresenter aren't named, so there's no Decorator seam
/// to hook into the same way. What IS a required, always-present named part in both frameworks'
/// ComboBox templates is the popup itself ("PART_Popup" - confirmed via ComboBox.cs, which
/// requires it via e.NameScope.Get). So this replaces PART_Popup's Child outright with a
/// DataGrid-hosting Border, rather than the WPF version's narrower Decorator.Child swap - same
/// end result (a DataGrid-backed dropdown instead of the normal item list), reusing 100% of the
/// real Fluent theme's chrome for everything else (closed-box border, placeholder text, dropdown
/// glyph). An earlier attempt built a complete replacement ControlTemplate from scratch instead;
/// that rendered essentially blank in a real headless screenshot (confirmed, not assumed) - almost
/// certainly missing some of what the Fluent ControlTheme's Setters normally provide - so this
/// popup-only-swap approach was used instead, both simpler and something a real screenshot
/// confirmed actually renders.
/// </summary>
public class LookupComboBox : CustomComboBox
{
    public static readonly StyledProperty<bool> AllowSetToDefaultProperty =
        AvaloniaProperty.Register<LookupComboBox, bool>(nameof(AllowSetToDefault), true);

    /// <summary>
    /// If set to true it is possible to set the ComboBox to its default value (= empty/null)
    /// e.g. by pressing the 'Delete' key when the ComboBox is not editable.
    /// </summary>
    public bool AllowSetToDefault
    {
        get => GetValue(AllowSetToDefaultProperty);
        set => SetValue(AllowSetToDefaultProperty, value);
    }

    protected DataGrid? DropDownDataGrid { get; private set; }

    public LookupComboBox()
    {
        // Same "package doesn't self-register its theme" gap Kapok.View.Avalonia.DefaultPageControls.
        // ListPageView already worked around for its own DataGrid - but that registration only
        // covers ListPageView's own subtree (Styles are scoped to where they're added), and a
        // LookupComboBox can be used anywhere, so it needs its own copy rather than relying on
        // being nested under a ListPageView. Confirmed via a real headless screenshot that without
        // this, the dropdown DataGrid gets real columns/rows (AutoGeneratingColumn ran correctly)
        // but renders at zero height - an unstyled DataGrid, not a missing-data bug.
        Styles.Add(new StyleInclude(new Uri("avares://Kapok.View.Avalonia/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        });

        DropDownOpened += (_, _) =>
        {
            // Matches WPF's OnDropDownOpened override - ComboBox doesn't expose that as an
            // overridable method in Avalonia (DropDownOpened/DropDownClosed are plain events
            // raised from PopupOpened/PopupClosed, confirmed via reflection - not protected
            // virtual OnDropDownOpened(EventArgs) like WPF's), so this subscribes instead.
            if (DropDownDataGrid?.SelectedItem is { } selected)
                DropDownDataGrid.ScrollIntoView(selected, null);
        };
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var popup = e.NameScope.Find<Popup>("PART_Popup");
        if (popup == null)
            return;

        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = true,
            IsReadOnly = true,
            CanUserSortColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MaxHeight = 300
        };
        dataGrid.Bind(DataGrid.ItemsSourceProperty, new Binding(nameof(ItemsSource)) { Source = this });
        dataGrid.Bind(DataGrid.SelectedItemProperty, new Binding(nameof(SelectedItem)) { Source = this, Mode = BindingMode.TwoWay });
        dataGrid.AutoGeneratingColumn += DropDownDataGrid_AutoGeneratingColumn;
        dataGrid.PointerReleased += DropDownDataGrid_PointerReleased;

        popup.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 252, 252, 252)),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = dataGrid
        };

        DropDownDataGrid = dataGrid;
    }

    private void DropDownDataGrid_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsDropDownOpen)
            return;

        // Closes the dropdown once a real cell (not the header or empty grid area) was clicked -
        // same intent as WPF's DataGridCell hit-test walk, adapted to Avalonia's visual tree API.
        if (e.Source is Visual visual && visual.GetSelfAndVisualAncestors().OfType<DataGridCell>().Any())
            IsDropDownOpen = false;
    }

    private void DropDownDataGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        // Avalonia's DataGridAutoGeneratingColumnEventArgs (unlike WPF's) only carries the
        // property's name/type, not a PropertyDescriptor/PropertyInfo - so the declaring item
        // type is recovered from the grid's own (already-populated - see AvaloniaPropertyLookupView.
        // GetItems and LookupComboBox's consumers) ItemsSource instead.
        var itemType = DropDownDataGrid?.ItemsSource?.Cast<object>().FirstOrDefault()?.GetType();
        var propertyInfo = itemType?.GetProperty(e.PropertyName);

        var lookupAttr = propertyInfo?.GetCustomAttribute<LookupColumnAttribute>();
        if (lookupAttr == null || !lookupAttr.Show)
        {
            e.Cancel = true;
            return;
        }

        if (propertyInfo?.GetCustomAttribute<BinaryImageAttribute>() != null)
        {
            // Avalonia's DataGrid has no DataGridImageColumn (confirmed: only Bound/CheckBox/
            // Template/Text column types exist) - a template column hosting an Image is the
            // direct equivalent. Not exercised by ToDoAvaloniaApp (no BinaryImageAttribute
            // property on Task/TaskList), ported for parity rather than silently dropped.
            var propertyName = e.PropertyName;
            e.Column = new DataGridTemplateColumn
            {
                Header = e.PropertyName,
                CellTemplate = new FuncDataTemplate<object>((_, _) =>
                {
                    var image = new Image { Stretch = Stretch.Uniform, MaxHeight = 40 };
                    image.Bind(Image.SourceProperty, new Binding(propertyName));
                    return image;
                })
            };
        }

        var displayAttribute = propertyInfo?.GetCustomAttribute<DisplayAttribute>();
        if (displayAttribute != null)
        {
            var resourceManager = (System.Resources.ResourceManager?)displayAttribute.ResourceType?
                .GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.Static)?.GetMethod?
                .Invoke(null, null);

            if (!string.IsNullOrEmpty(displayAttribute.Name))
            {
                var name = resourceManager?.GetString(displayAttribute.Name) ?? displayAttribute.Name;
                e.Column.Header = name;
            }
        }

        e.Column.IsReadOnly = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (!IsEditable && e.Key == Key.Delete && AllowSetToDefault)
        {
            SelectedValue = null;
        }

        base.OnKeyUp(e);
    }
}
