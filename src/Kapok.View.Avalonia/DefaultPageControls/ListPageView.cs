using System.Collections;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Kapok.View.Avalonia.Controls;

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
            IsReadOnly = true // cell editing is Excel-navigation territory, not built yet
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

        // Kapok's own column metadata - the same binding WPF's TableDataDataGrid style used.
        // It is normally still empty at this point (ListPage<TEntry> populates DataSet.Columns in
        // OnLoaded(), which runs after this control is attached), which is exactly why
        // CustomDataGrid tracks the collection's CollectionChanged rather than only reading it
        // once: the columns appear as soon as the page's current list view is applied, and plain
        // reflection-generated columns stand in until then.
        _dataGrid.ColumnsSource = (IList)dataSet.Columns;

        // DataGrid.SelectedItems turned out to be a get-only CLR property backed by its own
        // internal collection (no SelectedItemsProperty AvaloniaProperty exists to bind into,
        // unlike WPF's DataGrid, which needed CustomDataGrid's own bindable re-implementation for
        // the same reason) - syncing it to DataSet.SelectedEntries needs a two-way manual sync
        // (subscribe to both collections' CollectionChanged), not a plain Bind() call. Not built
        // yet - flagged rather than silently dropped.
    }
}
