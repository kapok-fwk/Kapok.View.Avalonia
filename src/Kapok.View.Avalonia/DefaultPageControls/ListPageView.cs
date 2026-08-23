using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Kapok.View.Avalonia.Controls;
using Kapok.View.Avalonia.Data;
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
/// (DataSet.SortAscendingAction etc.), not the page's Base menu. Phase 8 item 6 built the first
/// two (see BuildToolbar/Rebuild). The "edit filter" toggle is deliberately still deferred, not
/// silently dropped: WPF's Data/FilterSetView.cs is a genuinely substantial (~330 line) view model
/// - a whole second filter-editing UX (a filterable-property picker plus an editable list of
/// active filters) built on its own WPF-ICollectionView-wrapping helper,
/// Data/QueryableCollectionViewSource.cs, which has no Avalonia equivalent to build on (the same
/// gap Phase 8 item 4 already ran into for hierarchy navigation - see
/// AvaloniaHierarchyDataSetView's own header comment). Porting both would be its own workstream,
/// and the value is materially smaller here than it was for hierarchy navigation: Phase 7 item 3
/// already gave this port a real, working per-column inline filter row covering the same end-user
/// need (typing a filter expression per column), so this toggle would mostly duplicate existing
/// functionality in a different, WPF-shaped UI rather than add a missing capability.
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
    private readonly Button _sortAscendingButton;
    private readonly Button _sortDescendingButton;
    private readonly Button _listViewButton;
    private readonly TextBlock _listViewButtonText;
    private readonly MenuFlyout _listViewFlyout;
    private INotifyCollectionChanged? _observedListViews;

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

        (_sortAscendingButton, _sortDescendingButton, _listViewButton, _listViewButtonText, _listViewFlyout)
            = BuildToolbar();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(5, 2),
            Spacing = 2,
            Children = { _sortAscendingButton, _sortDescendingButton, _listViewButton }
        };

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(toolbar);
        Grid.SetRow(_dataGrid, 1);
        layout.Children.Add(_dataGrid);
        Content = layout;

        AttachedToVisualTree += (_, _) => Rebuild();
    }

    /// <summary>
    /// Sort-ascending/descending buttons and the list-view selector - matches WPF's
    /// ListPageControl.xaml ToolBar. Built once here (icons/flyout structure don't depend on the
    /// page), wired to the actual DataSet/page in <see cref="Rebuild"/> since that's the first
    /// point either is known.
    /// </summary>
    private static (Button SortAscending, Button SortDescending, Button ListView, TextBlock ListViewText, MenuFlyout ListViewFlyout) BuildToolbar()
    {
        Button IconButton(string imageName, string name) => new()
        {
            Name = name,
            Content = new Image
            {
                Width = 16,
                Height = 16,
                [!Image.SourceProperty] = new Binding
                {
                    Source = imageName,
                    Converter = new ImageNameToImageSourceConverter(),
                    ConverterParameter = "Small"
                }
            },
            Padding = new Thickness(4)
        };

        var sortAscending = IconButton("sort-az", "SortAscendingButton");
        var sortDescending = IconButton("sort-za", "SortDescendingButton");

        var listViewText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var listViewFlyout = new MenuFlyout();
        var listViewButton = new Button
        {
            Name = "ListViewButton",
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new Image
                    {
                        Width = 16,
                        Height = 16,
                        [!Image.SourceProperty] = new Binding
                        {
                            Source = "view-details",
                            Converter = new ImageNameToImageSourceConverter(),
                            ConverterParameter = "Small"
                        }
                    },
                    listViewText
                }
            },
            Flyout = listViewFlyout,
            Padding = new Thickness(4)
        };

        return (sortAscending, sortDescending, listViewButton, listViewText, listViewFlyout);
    }

    private void Rebuild()
    {
        if (DataContext is not IDataPage dataPage)
            return;

        var dataSet = dataPage.DataSet;
        if (dataSet == null)
            return;

        // Sort-ascending/descending buttons: WPF bound their Visibility to DataSet.CanUserSort
        // and their Command straight to DataSet.SortAscendingAction/SortDescendingAction - both
        // already fully functional on the core DataSetView (Load() sorts by SortBy/SortDirection),
        // just never reachable from any UI in this port until now.
        _sortAscendingButton.Command = dataSet.SortAscendingAction != null ? new ActionCommand(dataSet.SortAscendingAction) : null;
        _sortDescendingButton.Command = dataSet.SortDescendingAction != null ? new ActionCommand(dataSet.SortDescendingAction) : null;
        _sortAscendingButton.Bind(IsVisibleProperty, new Binding(nameof(IDataSetReadonlyView.CanUserSort)) { Source = dataSet, Mode = BindingMode.OneWay });
        _sortDescendingButton.Bind(IsVisibleProperty, new Binding(nameof(IDataSetReadonlyView.CanUserSort)) { Source = dataSet, Mode = BindingMode.OneWay });

        // List-view selector menu: matches WPF's MenuItem whose Header shows the current view's
        // caption and whose ItemsSource is DataSet.ListViews, each item's Command running its own
        // SelectAction (ListPage<TEntry>.ListViews_CollectionChanged wires that up already - see
        // its own comment). IListPage exposes both without needing reflection, unlike most of the
        // rest of this method (ListViews/CurrentListView were only added to the interface after
        // Phase 4's baseline was written).
        if (dataPage is IListPage listPage)
        {
            _listViewButton.IsVisible = true;
            _listViewButtonText.Bind(TextBlock.TextProperty, new Binding($"{nameof(IListPage.CurrentListView)}.{nameof(IDataSetListView.DisplayName)}")
            {
                Source = listPage,
                Converter = new CaptionConverter(),
                FallbackValue = "Standard view"
            });

            if (_observedListViews != null)
                _observedListViews.CollectionChanged -= ListViews_CollectionChanged;
            _observedListViews = listPage.ListViews as INotifyCollectionChanged;
            if (_observedListViews != null)
                _observedListViews.CollectionChanged += ListViews_CollectionChanged;

            RebuildListViewMenu(listPage);
        }
        else
        {
            _listViewButton.IsVisible = false;
        }

        var readonlyGenericInterface = dataSet.GetType().GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDataSetReadonlyView<>));

        if (readonlyGenericInterface == null)
            return;

        var entryType = readonlyGenericInterface.GetGenericArguments()[0];

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

        // Per-entity row colouring (DataGridStyling.xaml's CustomDataGridRowStyle) - the DataSet
        // decides a row's colours through Kapok's EntryColoring event.
        _dataGrid.ColoringDataSet = dataSet as IAvaloniaDataSetView;

        // Double-clicking a row opens its card page, matching WPF's
        // ListControlEntryMouseDoubleClickCommand: only when the page is *not* editable (an
        // editable list edits in place instead), and through the page's own OpenCardPageAction.
        // Declared on the concrete ListPage<TEntry>, so resolved by name like the actions above.
        var openCardPageProperty = dataPage.GetType().GetProperty("OpenCardPageAction");
        if (openCardPageProperty != null)
        {
            _dataGrid.RowActivated = entity =>
            {
                if (dataPage.IsEditable)
                    return;

                if (openCardPageProperty.GetValue(dataPage) is not { } openCardPageAction)
                    return;

                // IDataSetSelectionAction<TEntry>.Execute takes IList<TEntry>; the closed generic is
                // only known at runtime, so the one-element list is built for the actual entry type.
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType))!;
                list.Add(entity);

                var executeMethod = openCardPageAction.GetType().GetMethod(
                    nameof(IAction<object>.Execute), new[] { typeof(IList<>).MakeGenericType(entryType) });
                executeMethod?.Invoke(openCardPageAction, new object[] { list });
            };
        }

        // Drag & drop row reordering (item 6), offered only for entities that carry an explicit
        // order. ISortableEntity is the same gate core Kapok uses to decide whether a list page
        // even offers its SortUp/SortDown actions (see ListPage<TEntry>'s constructor), and it is
        // what lets a drop write the new order back onto the entities instead of being a
        // client-side shuffle that the next Refresh() discards.
        _dataGrid.CanUserReorderRows = typeof(Kapok.Entity.ISortableEntity).IsAssignableFrom(entryType);

        // Excel-style paste (item 5). CanUserPasteToNewRows follows DataSet.InsertAllowed, exactly
        // as WPF's TableDataDataGrid style bound it, and new rows are created through the DataSet's
        // own CreateNewEntryAction - the same business-layer path the "New" button takes, rather
        // than appending straight to the bound collection.
        _dataGrid.Bind(CustomDataGrid.CanUserPasteToNewRowsProperty,
            new Binding(nameof(IDataSetView.InsertAllowed)) { Source = dataSet, Mode = BindingMode.OneWay });
        _dataGrid.CreateNewRow = () =>
        {
            dataSet.CreateNewEntryAction.Execute();
            return dataSet.Current;
        };

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

    private void ListViews_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is IListPage listPage)
            RebuildListViewMenu(listPage);
    }

    /// <summary>
    /// Populates the list-view selector's flyout with one real MenuItem per
    /// <see cref="IDataSetListView"/>, each running its own SelectAction on click - built directly
    /// in code rather than through MenuItem.ItemsSource/an item template, since every other
    /// Kapok-specific piece of UI in this port already favours direct control construction over
    /// Avalonia's (less-verified-here) implicit item-container generation for small, rarely-
    /// changing lists like this one.
    /// </summary>
    private void RebuildListViewMenu(IListPage listPage)
    {
        _listViewFlyout.Items.Clear();

        foreach (var view in listPage.ListViews)
        {
            var menuItem = new MenuItem
            {
                Header = view.DisplayName?.LanguageOrDefault(CultureInfo.CurrentUICulture) ?? view.Name ?? view.ToString()
            };
            if (view.SelectAction != null)
                menuItem.Command = new ActionCommand(view.SelectAction);
            _listViewFlyout.Items.Add(menuItem);
        }
    }
}
