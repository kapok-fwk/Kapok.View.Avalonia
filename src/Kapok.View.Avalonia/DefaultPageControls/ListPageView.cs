using System.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Kapok.View.Avalonia.Controls;
using Kapok.View.Avalonia.ValueConverter;

namespace Kapok.View.Avalonia.DefaultPageControls;

/// <summary>
/// Generic IListPage control. Matches Kapok.View.Wpf's ListPageControl.xaml's grid, hosting a
/// <see cref="CustomDataGrid"/> - the Avalonia counterpart of WPF's CustomDataGrid - bound to the
/// page's DataSet.
///
/// Columns come from Kapok's own <c>DataSet.Columns</c> metadata
/// (<see cref="ColumnPropertyView"/>) via <see cref="CustomDataGrid.ColumnsSource"/>, the same
/// binding WPF's TableDataDataGrid style used. Plain CLR AutoGenerateColumns (what Phase 4's
/// native baseline used) remains the fallback for a DataSet with no column metadata at all - see
/// CustomDataGrid's AutoGeneratingColumn handler, which applies the same metadata treatment to
/// reflection-generated columns.
///
/// WPF's ListPageControl.xaml also has its own small toolbar here (sort-ascending/descending,
/// list-view selector, filter toggle) - those are DataSet-level, grid-specific controls
/// (DataSet.SortAscendingAction etc.), not the page's Base menu; still deferred.
///
/// DataSet.Collection is exposed only via the closed generic IDataSetReadonlyView&lt;TEntry&gt;
/// interface (DataSetView&lt;TEntry&gt; implements it as an explicit interface member on a protected
/// field), and this control is shared across every entity type, so TEntry isn't known at compile
/// time here. Resolved via one reflection lookup per page instead - it returns the actual live
/// ObservableCollection&lt;TEntry&gt; instance (not a copy), so the DataGrid still gets real
/// live updates through Avalonia's normal INotifyCollectionChanged binding support.
/// </summary>
public class ListPageView : UserControl
{
    private readonly CustomDataGrid _dataGrid;

    public ListPageView()
    {
        // Like AvaloniaControls.Ribbon.Desktop and Dock.Avalonia (see PageWindow/DockPageWindow's
        // Styles.Add comments), Avalonia.Controls.DataGrid ships its Fluent control theme as a
        // separate style resource, not auto-registered by referencing the package.
        Styles.Add(new StyleInclude(new Uri("avares://Kapok.View.Avalonia/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        });

        _dataGrid = new CustomDataGrid
        {
            CanUserSortColumns = true,
            CanUserResizeColumns = true,
            CanUserReorderColumns = true,
            // Matches WPF's TableDataDataGrid style (SelectionMode=Extended). Every
            // IDataSetSelectionAction<TEntry> - DeleteEntryAction, EditEntryAction, the Ribbon's
            // table-data buttons - takes a *list* of entries, so multi-selection is the contract,
            // not an optional extra.
            SelectionMode = DataGridSelectionMode.Extended
        };
        Content = _dataGrid;

        AttachedToVisualTree += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        if (DataContext is not IDataPage dataPage)
            return;

        var dataSet = dataPage.DataSet;
        if (dataSet == null)
            return;

        var readonlyGenericInterface = dataSet.GetType().GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDataSetReadonlyView<>));

        if (readonlyGenericInterface == null)
            return;

        var collectionProperty = readonlyGenericInterface.GetProperty(nameof(IDataSetReadonlyView<object>.Collection));
        if (collectionProperty?.GetValue(dataSet) is IEnumerable collection)
            _dataGrid.ItemsSource = collection;

        // Read-only state follows the page, matching WPF's TableDataDataGrid style
        // (IsReadOnly = {Binding IsEditable, Converter=InverseBoolean}). Phase 4's baseline pinned
        // this to true because no editor existed yet; the lookup column (item 4) has a real one, so
        // the grid now honours DataPage.IsEditable like the WPF version always did. Bound before
        // ColumnsSource: the column generator reads IsReadOnly while building each column.
        _dataGrid.Bind(DataGrid.IsReadOnlyProperty,
            new Binding(nameof(IDataPage.IsEditable))
            {
                Source = dataPage,
                Mode = BindingMode.OneWay,
                Converter = new InverseBooleanConverter()
            });

        // Per-column filter (item 3): bound before ColumnsSource so the header filter inputs can
        // resolve their view models as soon as the first column headers are realized.
        // DataSet.Filter is created in DataSetView's constructor, so UserLayer is always available
        // here, unlike DataSet.Columns.
        _dataGrid.Filter = dataSet.Filter.UserLayer;
        _dataGrid.Bind(CustomDataGrid.IsFilterVisibleProperty,
            new Binding(nameof(IDataSetReadonlyView.IsFilterVisible)) { Source = dataSet, Mode = BindingMode.OneWay });

        // ListPage<TEntry> creates its ToggleFilterVisibleAction with IsVisible = false (see its
        // constructor) and never turns it on - in WPF the filter row was reachable only through
        // that page action, so it was effectively unreachable there. Whether an in-grid filter row
        // exists at all is a property of the *view*, which is exactly what this control is, so this
        // view turns the action on. Resolved by name: ToggleFilterVisibleAction is declared on the
        // concrete ListPage<TEntry>, not on IDataPage.
        if (dataPage.GetType().GetProperty("ToggleFilterVisibleAction")?.GetValue(dataPage) is IToggleAction toggleFilterVisibleAction)
            toggleFilterVisibleAction.IsVisible = true;

        // Lookup / drill-down sources (item 4), also before ColumnsSource - the column generator
        // reads both while building each column. Equivalent to WPF's LookupItemsSource /
        // DrillDownActionDictionary bindings on its CustomDataGrid.
        //
        // Both collections are live references owned by PropertyViewCollection<TEntity>, which
        // fills them in its OnAdd *before* raising CollectionChanged - so assigning them once here,
        // even while DataSet.Columns is still empty, is enough: by the time a column is generated,
        // its lookup view / drill-down action is already registered.
        _dataGrid.LookupViews = dataSet.Columns.LookupViews;

        // DrillDown is declared on the closed generic IPropertyViewCollection<TEntity> only (the
        // non-generic interface carries LookupViews but not DrillDown), so it is read by name -
        // same reflection-by-necessity as the Collection lookup above.
        _dataGrid.DrillDownActionDictionary =
            dataSet.Columns.GetType().GetProperty(nameof(IPropertyViewCollection<object>.DrillDown))?.GetValue(dataSet.Columns) as IDictionary;

        // Kapok's own column metadata - the same binding WPF's TableDataDataGrid style used.
        // It is normally still empty at this point (ListPage<TEntry> populates DataSet.Columns in
        // OnLoaded(), which runs after this control is attached), which is exactly why
        // CustomDataGrid tracks the collection's CollectionChanged rather than only reading it
        // once: the columns appear as soon as the page's current list view is applied, and plain
        // reflection-generated columns stand in until then.
        _dataGrid.ColumnsSource = (IList)dataSet.Columns;

        // Selection, matching WPF's TableDataDataGrid style's SelectedItem/SelectedItems setters.
        // DataGrid.SelectedItems is a get-only CLR property in Avalonia with no AvaloniaProperty
        // behind it, so the multi-selection side goes through CustomDataGrid.SelectedEntries (see
        // that property) rather than a direct Bind() - the single-selection side binds natively.
        _dataGrid.Bind(DataGrid.SelectedItemProperty,
            new Binding(nameof(IDataSetReadonlyView.Current)) { Source = dataSet, Mode = BindingMode.TwoWay });
        _dataGrid.Bind(CustomDataGrid.SelectedEntriesProperty,
            new Binding(nameof(IDataSetReadonlyView.SelectedEntries)) { Source = dataSet, Mode = BindingMode.TwoWay });

        // Ctrl+A -> DataSet.SelectAllAction, the same KeyBinding WPF's ListPageControl.xaml
        // declares on its CustomDataGrid. SelectAllAction is declared on the concrete
        // DataSetView<TEntry> class, not on any IDataSetView interface (checked - the interfaces
        // only carry SortAscendingAction/SortDescendingAction/ToggleFilterVisibleAction/
        // ClearUserFilterAction), so it is resolved by name; a page whose DataSet does not have it
        // simply gets no shortcut rather than an exception.
        if (dataSet.GetType().GetProperty("SelectAllAction")?.GetValue(dataSet) is IAction selectAllAction)
        {
            _dataGrid.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.A, KeyModifiers.Control),
                Command = new ActionCommand(selectAllAction)
            });
        }
    }
}
