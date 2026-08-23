using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Kapok.BusinessLayer;
using Kapok.Entity;
using Kapok.View.Avalonia.Helper;
using Kapok.View.Avalonia.ValueConverter;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// Extends Avalonia's <see cref="DataGrid"/> with the Kapok-specific functionality its WPF
/// counterpart (Kapok.View.Wpf's <c>CustomDataGrid</c>, 1,328 lines) adds on top of a plain
/// DataGrid.
///
/// Deliberately *not* a 1:1 port of that file: Phase 4's audit (done against the real installed
/// Avalonia.Controls.DataGrid via reflection, not against WPF's feature list) confirmed that
/// column sort/resize/reorder, multi-selection, Excel-style clipboard *copy*, the cell/row edit
/// lifecycle and frozen columns are all already native here, so the WPF re-implementations of
/// those are not carried over. What remains - and is what this class actually is - is the
/// genuinely Kapok-specific part:
///
///  - <see cref="ColumnsSource"/>: build the grid's columns from Kapok's own
///    <see cref="ColumnPropertyView"/> metadata instead of plain CLR reflection.
///
/// Note on the DataGrid theme: like AvaloniaControls.Ribbon.Desktop.Flowery and Dock.Avalonia
/// (see PageWindow/DockPageWindow), Avalonia.Controls.DataGrid ships its Fluent control theme as a
/// separate style resource that referencing the package does not auto-register. Consumers of this
/// control are responsible for including it (see ListPageView) - it is registered per subtree, so
/// registering it here as well would just duplicate it for every grid instance.
/// </summary>
public class CustomDataGrid : DataGrid
{
    /// <summary>
    /// Avalonia's implicit ControlTheme lookup keys strictly off StyleKeyOverride (GetType() by
    /// default, with no base-type fallback - see StyledElement.GetEffectiveTheme), so without this
    /// a CustomDataGrid gets no template at all from Avalonia.Controls.DataGrid's Fluent theme,
    /// which is keyed on typeof(DataGrid). Same trap already hit in Phase 5 by
    /// CustomComboBox/LookupComboBox; this is the standard Avalonia idiom for "inherit a base
    /// control's theme in a subclass".
    /// </summary>
    protected override Type StyleKeyOverride => typeof(DataGrid);

    /// <summary>Cell style class marking a numeric column - its text is right aligned.</summary>
    public const string NumericCellClass = "kapok-numeric";

    /// <summary>Cell style class marking a text-wrapping column.</summary>
    public const string TextWrapCellClass = "kapok-textwrap";

    /// <summary>Cell style class marking a Guid column - trimmed with an ellipsis.</summary>
    public const string GuidCellClass = "kapok-guid";

    public CustomDataGrid()
    {
        // WPF applied these per column through DataGridTextColumn.ElementStyle (a Style targeting
        // the generated TextBlock). Avalonia's DataGridTextColumn has no ElementStyle, but
        // DataGridColumn.CellStyleClasses puts arbitrary style classes on the generated
        // DataGridCell - so the same effect is achieved with real Avalonia style selectors
        // matching the TextBlock inside a classed cell. This is the native extensibility point,
        // not a workaround.
        Styles.Add(new Style(x => x.OfType<DataGridCell>().Class(NumericCellClass).Descendant().OfType<TextBlock>())
        {
            Setters = { new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right) }
        });
        Styles.Add(new Style(x => x.OfType<DataGridCell>().Class(NumericCellClass).Descendant().OfType<TextBox>())
        {
            Setters = { new Setter(TextBox.HorizontalContentAlignmentProperty, HorizontalAlignment.Right) }
        });
        Styles.Add(new Style(x => x.OfType<DataGridCell>().Class(TextWrapCellClass).Descendant().OfType<TextBlock>())
        {
            Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) }
        });
        Styles.Add(new Style(x => x.OfType<DataGridCell>().Class(GuidCellClass).Descendant().OfType<TextBlock>())
        {
            Setters = { new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis) }
        });

        AutoGeneratingColumn += OnAutoGeneratingColumnApplyMetadata;
        SelectionChanged += OnSelectionChangedSyncSelectedEntries;

        // Tunnel, not bubble: WPF used the Preview* mouse events so the drag could be recognised
        // before the cell/row handled the press. Avalonia's equivalent is an explicitly registered
        // tunnelling handler - overriding OnPointerPressed would only see presses the DataGrid's own
        // row/cell handling did not already consume. The press handler deliberately does not mark
        // the event handled, so normal selection still works, same as WPF.
        AddHandler(PointerPressedEvent, OnPointerPressedStartRowDrag, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMovedDragRow, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleasedDropRow, RoutingStrategies.Tunnel);
    }

    #region Drag & drop row reordering

    /// <summary>
    /// Whether rows can be reordered by dragging them.
    ///
    /// **Note on the WPF original**: its CustomDataGrid gated the whole feature on
    /// <c>DragPopup != null</c>, and *nothing in Kapok.View.Wpf ever sets DragPopup* -
    /// grep-confirmed across the whole module, including DataGridStyling.xaml. So
    /// <c>OnMouseLeftButtonDown_DragDropRow</c> always returned on its first line and row
    /// drag-and-drop could never actually run there: dead code, the same finding Phase 5 made about
    /// <c>FlatComboBoxStyle.xaml</c> and item 3 made about <c>FilterType.cs</c>. It is ported here
    /// as a working feature rather than a dead one, with an explicit opt-in property instead of
    /// "did someone remember to assign a popup".
    /// </summary>
    public static readonly StyledProperty<bool> CanUserReorderRowsProperty =
        AvaloniaProperty.Register<CustomDataGrid, bool>(nameof(CanUserReorderRows));

    public bool CanUserReorderRows
    {
        get => GetValue(CanUserReorderRowsProperty);
        set => SetValue(CanUserReorderRowsProperty, value);
    }

    /// <summary>The row currently being dragged, or null. Same role as WPF's DraggedItem.</summary>
    public static readonly StyledProperty<object?> DraggedItemProperty =
        AvaloniaProperty.Register<CustomDataGrid, object?>(nameof(DraggedItem));

    public object? DraggedItem
    {
        get => GetValue(DraggedItemProperty);
        set => SetValue(DraggedItemProperty, value);
    }

    /// <summary>
    /// The "ghost" shown under the pointer while a row is being dragged. Optional here - the grid
    /// builds a default one on first use if the host did not supply it, which is what stops this
    /// feature from being unreachable the way WPF's was.
    /// </summary>
    public static readonly StyledProperty<Popup?> DragPopupProperty =
        AvaloniaProperty.Register<CustomDataGrid, Popup?>(nameof(DragPopup));

    public Popup? DragPopup
    {
        get => GetValue(DragPopupProperty);
        set => SetValue(DragPopupProperty, value);
    }

    private bool _isDraggingRow;
    private bool _dragPopupUnavailable;
    private bool _temporaryDragDropIsReadOnly;
    private object? _dropTargetItem;
    private Point _dragStartPoint;

    /// <summary>Pointer distance before a press turns into a row drag rather than a click.</summary>
    private const double DragThreshold = 4;

    private void OnPointerPressedStartRowDrag(object? sender, PointerPressedEventArgs e)
    {
        if (!CanUserReorderRows || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // WPF additionally bailed out when ItemsSource was not an IList; here that is checked at
        // drop time instead (MoveRow), so a grid over a read-only sequence simply never completes a
        // drag rather than behaving differently on press.
        var row = FindRow(e);
        if (row?.DataContext == null)
            return;

        _isDraggingRow = true;
        _dragStartPoint = e.GetPosition(this);
        SetCurrentValue(DraggedItemProperty, row.DataContext);
    }

    private void OnPointerMovedDragRow(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingRow || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var position = e.GetPosition(this);

        // A plain click must not start a reorder. WPF had no threshold because its feature never
        // ran at all; without one, every click would flip the grid into read-only mode and pop up a
        // drag ghost.
        if (Math.Abs(position.X - _dragStartPoint.X) < DragThreshold &&
            Math.Abs(position.Y - _dragStartPoint.Y) < DragThreshold)
            return;

        var popup = DragPopup ??= BuildDefaultDragPopup();

        if (!popup.IsOpen)
        {
            // Switch to read-only while dragging, exactly as WPF did - a drag must not start a cell
            // edit on the way past.
            if (!IsReadOnly)
            {
                _temporaryDragDropIsReadOnly = true;
                SetCurrentValue(IsReadOnlyProperty, true);
            }

            popup.PlacementTarget = this;

            try
            {
                popup.IsOpen = true;
            }
            catch (InvalidOperationException)
            {
                // "Unable to create IPopupImpl and no overlay layer is found for the target
                // control" - the same limitation Phase 5 documented for LookupComboBox's dropdown:
                // AvaloniaControls.Ribbon.Desktop.Flowery's Window template has no popup overlay
                // layer, and a real desktop backend never takes that path because it can always
                // create a native popup window. The ghost is decoration; a drag must not be
                // abandoned because it could not be shown, so this is swallowed and the reorder
                // carries on.
                //
                // Note IsOpen still reads true afterwards - Popup sets the property first and only
                // then fails to create the platform impl - which is why the offsets below are
                // guarded by this flag rather than by IsOpen.
                _dragPopupUnavailable = true;
            }
        }

        if (!_dragPopupUnavailable)
        {
            popup.HorizontalOffset = position.X;
            popup.VerticalOffset = position.Y;
        }

        _dropTargetItem = FindRow(e)?.DataContext;
    }

    private void OnPointerReleasedDropRow(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingRow)
            return;

        var draggedItem = DraggedItem;
        var targetItem = _dropTargetItem;

        ResetRowDrag();

        if (draggedItem != null && targetItem != null && !ReferenceEquals(draggedItem, targetItem))
            MoveRow(draggedItem, targetItem);
    }

    private void ResetRowDrag()
    {
        _isDraggingRow = false;
        _dropTargetItem = null;
        SetCurrentValue(DraggedItemProperty, null);

        if (DragPopup != null)
        {
            try
            {
                DragPopup.IsOpen = false;
            }
            catch (InvalidOperationException)
            {
                // See the matching catch when opening it.
            }
        }

        _dragPopupUnavailable = false;

        if (_temporaryDragDropIsReadOnly)
        {
            SetCurrentValue(IsReadOnlyProperty, false);
            _temporaryDragDropIsReadOnly = false;
        }
    }

    /// <summary>
    /// Moves <paramref name="draggedItem"/> to <paramref name="targetItem"/>'s position. Public so
    /// the drop can be driven (and verified) without simulating a pointer drag.
    /// </summary>
    /// <returns><c>true</c> when the collection was actually reordered.</returns>
    public bool MoveRow(object draggedItem, object targetItem)
    {
        if (ItemsSource is not IList list)
            return false;

        var targetIndex = list.IndexOf(targetItem);
        var oldIndex = list.IndexOf(draggedItem);
        if (targetIndex < 0 || oldIndex < 0 || targetIndex == oldIndex)
            return false;

        // ObservableCollection<T>.Move is the non-destructive path (one Move notification rather
        // than a Remove+Insert pair), and is what WPF reached for too - via reflection, because it
        // only had the non-generic IList. Same here, for the same reason.
        var moveMethod = list.GetType().GetMethod("Move", new[] { typeof(int), typeof(int) });
        if (moveMethod != null)
        {
            moveMethod.Invoke(list, new object[] { oldIndex, targetIndex });
        }
        else
        {
            list.RemoveAt(oldIndex);
            list.Insert(targetIndex, draggedItem);
        }

        SetCurrentValue(SelectedItemProperty, draggedItem);

        RenumberSortOrder(list);
        return true;
    }

    /// <summary>
    /// Writes the new visual order back onto the entities when they carry one.
    ///
    /// WPF stopped at moving the item inside the collection, which is a purely client-side shuffle
    /// that any Refresh() throws away. Kapok already has the concept for a persisted order -
    /// <see cref="ISortableEntity"/>, the same interface <c>ListPage&lt;TEntry&gt;</c> uses to
    /// decide whether to offer its SortUp/SortDown actions at all, and which
    /// <c>SortableDataSetView.SortUp</c> maintains by rewriting SortOrder - so a drag-drop reorder
    /// maintains it the same way. For an entity without it the move stays client-side, exactly as
    /// in WPF.
    /// </summary>
    private static void RenumberSortOrder(IList list)
    {
        if (list.Count == 0 || list[0] is not ISortableEntity)
            return;

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is ISortableEntity sortable)
                sortable.SortOrder = i + 1;
        }
    }

    /// <summary>
    /// The row a pointer event happened over.
    ///
    /// The routed event's own Source is the primary answer - it is the cell/row the input system
    /// already resolved, and it is what WPF's <c>FindFromPoint</c> hit test was approximating. The
    /// geometric hit test is only a fallback for events raised without a meaningful source; note it
    /// returns null under Avalonia.Headless (confirmed here: a correctly-positioned press hit-tested
    /// to nothing), which is another reason not to depend on it.
    /// </summary>
    private DataGridRow? FindRow(PointerEventArgs e)
        => (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<DataGridRow>().FirstOrDefault()
           ?? FindRowAt(e.GetPosition(this));

    private DataGridRow? FindRowAt(Point position)
        => (this.InputHitTest(position) as Visual)?
            .GetSelfAndVisualAncestors()
            .OfType<DataGridRow>()
            .FirstOrDefault();

    /// <summary>
    /// The default drag ghost: a small bordered label showing the dragged row. WPF expected the
    /// host to provide one and no host ever did (see <see cref="CanUserReorderRows"/>).
    /// </summary>
    private Popup BuildDefaultDragPopup()
    {
        var text = new TextBlock { Margin = new Thickness(6, 3), VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(DraggedItem)) { Source = this });

        return new Popup
        {
            IsLightDismissEnabled = false,
            Placement = PlacementMode.TopEdgeAlignedLeft,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 250, 250, 250)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = text
            }
        };
    }

    #endregion

    #region Clipboard paste

    /// <summary>
    /// Whether pasting more rows than the grid currently has may create new entries. Bound to
    /// <c>DataSet.InsertAllowed</c>, matching WPF's CustomDataGrid property of the same name.
    /// </summary>
    public static readonly StyledProperty<bool> CanUserPasteToNewRowsProperty =
        AvaloniaProperty.Register<CustomDataGrid, bool>(nameof(CanUserPasteToNewRows), defaultValue: true);

    public bool CanUserPasteToNewRows
    {
        get => GetValue(CanUserPasteToNewRowsProperty);
        set => SetValue(CanUserPasteToNewRowsProperty, value);
    }

    /// <summary>
    /// Creates one new entry through the business layer and returns it, or null when the host has
    /// not supplied a factory.
    ///
    /// WPF grew the list by driving its DataGrid's <c>NewItemPlaceholder</c> row through
    /// <c>IEditableCollectionView.AddNew()</c>. Avalonia's DataGrid has neither concept, and adding
    /// straight to the bound ObservableCollection would bypass the business layer entirely (no
    /// <c>InitNewEntry</c>, no <c>AddingNewEntry</c>/<c>NewEntryAdded</c> events). A factory the
    /// host wires to <c>DataSet.CreateNewEntryAction</c> keeps paste on the same path as the "New"
    /// button - see ListPageView.
    /// </summary>
    public Func<object?>? CreateNewRow { get; set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // WPF registered a class CommandBinding for ApplicationCommands.Paste. Avalonia has no
        // application-command routing, so Ctrl+V (Cmd+V on macOS) is handled directly. Fire and
        // forget is deliberate: the clipboard API is async all the way down and OnKeyDown cannot
        // await; PasteAsync itself never throws out.
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(PlatformPasteModifier) && !IsReadOnly)
        {
            e.Handled = true;
            _ = PasteAsync();
            return;
        }

        base.OnKeyDown(e);
    }

    private static KeyModifiers PlatformPasteModifier =>
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>
    /// Pastes the clipboard's tabular content into the grid, starting at the current cell.
    ///
    /// Port of WPF CustomDataGrid's OnExecutedPaste. The shape of the loop (start at the current
    /// row/column, stop when the clipboard runs out, skip read-only columns) is the same; what
    /// differs is how a value reaches the entity. WPF called
    /// <c>DataGridColumn.OnPastingCellClipboardContent</c>, which Avalonia's DataGridColumn does not
    /// have - so the target property is resolved from the column's own
    /// <see cref="DataGridColumn.ClipboardContentBinding"/> (which every column this grid generates
    /// sets, and which DataGridBoundColumn derives from its Binding) and assigned with a real type
    /// conversion.
    /// </summary>
    /// <returns>The number of cells actually written.</returns>
    public async Task<int> PasteAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        var rowData = await ClipboardHelper.ParseClipboardDataAsync(clipboard).ConfigureAwait(true);

        return PasteRows(rowData);
    }

    /// <summary>
    /// The paste itself, separated from reading the clipboard so it can be driven from data
    /// directly (verification, or a caller that already has the rows).
    /// </summary>
    public int PasteRows(IReadOnlyList<object?[]> rowData)
    {
        if (rowData.Count == 0 || IsReadOnly)
            return 0;

        var items = ItemsSource?.Cast<object>().ToList() ?? new List<object>();

        var firstRowIndex = CurrentItemIndex(items);
        var targetColumns = ColumnsFromCurrent();
        if (targetColumns.Count == 0)
            return 0;

        var pastedCells = 0;

        for (var rowDataIndex = 0; rowDataIndex < rowData.Count; rowDataIndex++)
        {
            var itemIndex = firstRowIndex + rowDataIndex;

            object? item;
            if (itemIndex < items.Count)
            {
                item = items[itemIndex];
            }
            else
            {
                if (!CanUserPasteToNewRows)
                    break;

                item = CreateNewRow?.Invoke();
                if (item == null)
                    break;

                items.Add(item);
            }

            var cells = rowData[rowDataIndex];
            var cellCount = Math.Min(cells.Length, targetColumns.Count);

            for (var cellIndex = 0; cellIndex < cellCount; cellIndex++)
            {
                var column = targetColumns[cellIndex];
                if (column.IsReadOnly)
                    continue;

                if (TrySetCellValue(item, column, cells[cellIndex]))
                    pastedCells++;
            }
        }

        return pastedCells;
    }

    /// <summary>Index of the row the paste starts at - the current row, or the first one.</summary>
    private int CurrentItemIndex(List<object> items)
    {
        if (SelectedItem != null)
        {
            var index = items.IndexOf(SelectedItem);
            if (index >= 0)
                return index;
        }

        return 0;
    }

    /// <summary>
    /// The visible columns from the current one rightwards, in display order - matching WPF's
    /// ColumnFromDisplayIndex walk from <c>CurrentColumn</c> to the last column.
    /// </summary>
    private List<DataGridColumn> ColumnsFromCurrent()
    {
        var ordered = Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex).ToList();

        var startIndex = CurrentColumn != null ? ordered.IndexOf(CurrentColumn) : 0;
        if (startIndex < 0)
            startIndex = 0;

        return ordered.Skip(startIndex).ToList();
    }

    /// <summary>
    /// Writes one clipboard value into one entity property, converting it to the property's type.
    /// </summary>
    private static bool TrySetCellValue(object item, DataGridColumn column, object? value)
    {
        var path = (column.ClipboardContentBinding as Binding)?.Path;
        if (string.IsNullOrEmpty(path))
            return false;

        var propertyInfo = item.GetType().GetProperty(path);
        if (propertyInfo?.SetMethod == null)
            return false;

        if (!TryConvert(value, propertyInfo.PropertyType, out var converted))
            return false;

        propertyInfo.SetValue(item, converted);
        return true;
    }

    /// <summary>
    /// Converts a clipboard cell to the target property's type. Clipboard values arrive either as
    /// strings (CSV/text) or already typed (Excel's XML Spreadsheet carries DateTime/decimal - see
    /// ClipboardHelper), so both cases are handled. A value that cannot be converted is skipped
    /// rather than throwing: a paste covering several columns must not be aborted halfway by one
    /// bad cell.
    /// </summary>
    private static bool TryConvert(object? value, Type targetType, out object? converted)
    {
        converted = null;

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value == null || (value is string text && text.Length == 0))
        {
            // An empty cell clears the property where that is representable: a string becomes
            // empty, a nullable or reference type becomes null. A non-nullable value type has no
            // "empty", so the cell is skipped rather than being forced to default(T) - pasting a
            // blank must not silently write a 0 or a 01.01.0001.
            if (targetType == typeof(string))
            {
                converted = string.Empty;
                return true;
            }

            if (!targetType.IsValueType || targetType != underlyingType)
            {
                converted = null;
                return true;
            }

            return false;
        }

        if (underlyingType.IsInstanceOfType(value))
        {
            converted = value;
            return true;
        }

        try
        {
            if (underlyingType.IsEnum)
            {
                converted = Enum.Parse(underlyingType, value.ToString()!, ignoreCase: true);
                return true;
            }

            if (underlyingType == typeof(Guid))
            {
                converted = Guid.Parse(value.ToString()!);
                return true;
            }

            converted = Convert.ChangeType(value, underlyingType, CultureInfo.CurrentCulture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    #endregion

    #region Lookup / drill-down sources

    /// <summary>
    /// The DataSet's per-property lookup views (<c>DataSet.Columns.LookupViews</c>) - the entries a
    /// <see cref="DataGridLookupComboBoxColumn"/> offers. Equivalent to WPF's
    /// <c>LookupItemsSource</c> binding, but a plain reference rather than a
    /// <c>BindingBase</c> whose path each generated column had to re-derive as a string.
    /// </summary>
    public IReadOnlyDictionary<string, IPropertyLookupView>? LookupViews { get; set; }

    /// <summary>
    /// The DataSet's per-property drill-down actions (<c>DataSet.Columns.DrillDown</c>, an
    /// <c>IReadOnlyDictionary&lt;string, IDataSetSelectionAction&lt;TEntry&gt;&gt;</c> reached here
    /// through the non-generic <see cref="IDictionary"/>). Same role as WPF's
    /// <c>DrillDownActionDictionary</c>.
    /// </summary>
    public IDictionary? DrillDownActionDictionary { get; set; }

    #endregion

    #region Per-column filter

    /// <summary>
    /// The DataSet's user filter layer (<c>DataSet.Filter.UserLayer</c>) - the collection each
    /// column's filter input adds/updates/removes its own
    /// <see cref="Kapok.BusinessLayer.PropertyFilterStringFilter"/> in. Same binding WPF's
    /// TableDataDataGrid style used.
    /// </summary>
    public static readonly StyledProperty<IPropertyFilterCollection?> FilterProperty =
        AvaloniaProperty.Register<CustomDataGrid, IPropertyFilterCollection?>(nameof(Filter));

    public IPropertyFilterCollection? Filter
    {
        get => GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }

    /// <summary>
    /// Whether the per-column filter row is shown under the column captions - bound to
    /// <c>DataSet.IsFilterVisible</c>, which the page's <c>ToggleFilterVisibleAction</c> toggles
    /// (the "Clear filter"/filter buttons in the Ribbon's Page group).
    /// </summary>
    public static readonly StyledProperty<bool> IsFilterVisibleProperty =
        AvaloniaProperty.Register<CustomDataGrid, bool>(nameof(IsFilterVisible));

    public bool IsFilterVisible
    {
        get => GetValue(IsFilterVisibleProperty);
        set => SetValue(IsFilterVisibleProperty, value);
    }

    /// <summary>
    /// Creates the filter view model for one column, or null when this grid has no filter bound
    /// (a page whose DataSet exposes no user filter layer simply gets no filter row).
    /// </summary>
    private DataGridColumnFilterViewModel? CreateColumnFilterViewModel(string propertyName)
    {
        var filter = Filter;
        if (filter == null)
            return null;

        var entryType = GetEntryType();
        if (entryType == typeof(object) || entryType.GetProperty(propertyName) == null)
            return null;

        return new DataGridColumnFilterViewModel(filter, entryType, propertyName);
    }

    #endregion

    #region Bindable multi-selection 'SelectedEntries'

    /// <summary>
    /// Two-way mirror of <c>DataSet.SelectedEntries</c> - the list every
    /// <see cref="IDataSetSelectionAction{TEntry}"/> (DeleteEntryAction, EditEntryAction, the
    /// Ribbon's table-data buttons, ...) actually operates on.
    ///
    /// Needed because <see cref="DataGrid.SelectedItems"/> is a get-only CLR property in Avalonia
    /// with no AvaloniaProperty behind it, so it cannot be bound at all (WPF's DataGrid had the
    /// same gap, which is why its CustomDataGrid declared its own <c>SelectedItems</c>
    /// DependencyProperty).
    /// </summary>
    public static readonly StyledProperty<IList?> SelectedEntriesProperty =
        AvaloniaProperty.Register<CustomDataGrid, IList?>(
            nameof(SelectedEntries), defaultBindingMode: BindingMode.TwoWay);

    public IList? SelectedEntries
    {
        get => GetValue(SelectedEntriesProperty);
        set => SetValue(SelectedEntriesProperty, value);
    }

    /// <summary>Guards the two sync directions against re-entering each other.</summary>
    private bool _syncingSelection;

    private void OnSelectionChangedSyncSelectedEntries(object? sender, SelectionChangedEventArgs e)
    {
        // Matches WPF's OnSelectionChanged_MoveToSelectedEntry: keep the newly selected row
        // visible. Avalonia's ScrollIntoView takes an explicit column (null = the current one).
        if (e.AddedItems.Count == 1)
            ScrollIntoView(e.AddedItems[0]!, null);

        if (_syncingSelection)
            return;

        _syncingSelection = true;
        try
        {
            SetCurrentValue(SelectedEntriesProperty, CreateSelectionSnapshot());
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// Copies the grid's current selection into a fresh, strongly typed
    /// <c>List&lt;TEntry&gt;</c>.
    ///
    /// Both parts matter. **Fresh**: <c>DataSetView.SelectedEntries</c> is a plain property raising
    /// PropertyChanged on assignment (not an observable collection), so handing it the grid's own
    /// live SelectedItems instance - which is what WPF's CustomDataGrid did - would notify exactly
    /// once and then silently mutate underneath every binding. **Strongly typed**: consumers cast
    /// the value to <c>IList&lt;TEntry&gt;</c> (see ActionCommand.ForGeneric, which the Ribbon's
    /// table-data buttons are built on, and DataSetView's own
    /// <c>IDataSetReadonlyView&lt;TEntry&gt;.SelectedEntries</c> accessor), so a
    /// <c>List&lt;object&gt;</c> would throw an InvalidCastException on the first click.
    /// </summary>
    private IList CreateSelectionSnapshot()
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(GetEntryType()))!;
        foreach (var item in SelectedItems)
            list.Add(item);
        return list;
    }

    /// <summary>
    /// The entity type of the bound collection. Read off ItemsSource's own
    /// <c>IEnumerable&lt;T&gt;</c> - the DataSet's live <c>ObservableCollection&lt;TEntry&gt;</c> -
    /// since this control is shared across every entity type and has no TEntry of its own.
    /// </summary>
    private Type GetEntryType()
    {
        var enumerableInterface = ItemsSource?.GetType().GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableInterface?.GetGenericArguments()[0]
               ?? ItemsSource?.Cast<object>().FirstOrDefault()?.GetType()
               ?? typeof(object);
    }

    /// <summary>
    /// Applies a selection coming from the data side (e.g. <c>DataSet.SelectAllAction</c>, which
    /// assigns <c>Collection.ToList()</c>) to the grid.
    ///
    /// WPF's equivalent simply called <c>SelectAll()</c> and ignored the incoming list entirely -
    /// correct only for the one caller that happened to exist. Avalonia's SelectedItems collection
    /// is mutable, so the actual list can be applied here.
    /// </summary>
    private void ApplySelectedEntriesToSelection(IList? entries)
    {
        _syncingSelection = true;
        try
        {
            SelectedItems.Clear();

            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry != null)
                    SelectedItems.Add(entry);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    #endregion

    #region Bindable columns 'ColumnsSource'

    /// <summary>
    /// The Kapok column metadata the grid's columns are generated from - normally bound to
    /// <c>DataSet.Columns</c> (an <see cref="IPropertyViewCollection"/> of
    /// <see cref="ColumnPropertyView"/>). Setting it switches <see cref="DataGrid.AutoGenerateColumns"/>
    /// off, exactly like WPF's CustomDataGrid does.
    /// </summary>
    public static readonly StyledProperty<IList?> ColumnsSourceProperty =
        AvaloniaProperty.Register<CustomDataGrid, IList?>(nameof(ColumnsSource));

    public IList? ColumnsSource
    {
        get => GetValue(ColumnsSourceProperty);
        set => SetValue(ColumnsSourceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColumnsSourceProperty)
            OnColumnsSourceChanged(change.GetOldValue<IList?>(), change.GetNewValue<IList?>());
        else if (change.Property == SelectedEntriesProperty && !_syncingSelection)
            ApplySelectedEntriesToSelection(change.GetNewValue<IList?>());
    }

    private void OnColumnsSourceChanged(IList? oldValue, IList? newValue)
    {
        if (oldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= ColumnsSource_CollectionChanged;

        // WPF needed a WeakEventManager here, plus a special case digging out
        // PropertyViewCollection<T>'s internal ObservableCollection via ICollection.SyncRoot,
        // because CollectionChangedEventManager refuses to forward events whose sender is not the
        // nominal source object. Avalonia has no such event-manager layer: a plain CLR event
        // subscription on the IPropertyViewCollection itself works, and PropertyViewCollection<T>
        // already re-exposes its inner collection's CollectionChanged event as its own (see its
        // INotifyCollectionChanged region), so the SyncRoot detour is unnecessary here.
        // Unsubscribing above/here is what keeps this leak-free.
        ResetColumnsFromColumnsSource(newValue);

        if (newValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += ColumnsSource_CollectionChanged;
    }

    private void ColumnsSource_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // WPF had to force an explicit CommitEdit/CancelEdit here to work around its DataGrid
        // caching editing cells in _editingCellAutomationValueHolders (which keep a reference to
        // the DataGridColumn and crash once it is detached - see dotnet/wpf#6553). Avalonia's
        // DataGrid has no such automation-value cache, but ending the edit is still correct
        // behaviour: the column the user is editing may be the one being removed.
        CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        // While the fallback (plain auto-generated) columns are in place, the grid's Columns and
        // ColumnsSource are not in sync index-for-index, so an incremental Add/Remove/Move cannot
        // be applied - the arrival of the first real metadata column means switching the whole
        // grid over to metadata-driven columns. This is what makes the control tolerant of the
        // real page lifecycle: ListPage<TEntry> populates DataSet.Columns in OnLoaded(), i.e.
        // *after* the view has already been created and bound.
        if (AutoGenerateColumns)
        {
            ResetColumnsFromColumnsSource(ColumnsSource);
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (ColumnPropertyView column in e.NewItems!)
                    AddColumnFromColumnPropertyView(column);
                break;
            case NotifyCollectionChangedAction.Remove:
                foreach (ColumnPropertyView column in e.OldItems!)
                    RemoveColumnByColumnPropertyView(column);
                break;
            case NotifyCollectionChangedAction.Replace:
                foreach (ColumnPropertyView column in e.OldItems!)
                    RemoveColumnByColumnPropertyView(column);
                foreach (ColumnPropertyView column in e.NewItems!)
                    AddColumnFromColumnPropertyView(column);
                break;
            case NotifyCollectionChangedAction.Move:
                Columns.Move(e.OldStartingIndex, e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Reset:
                // ObservableCollection's Reset carries no items (NewItems is null) - the
                // authoritative current content is ColumnsSource itself, which WPF's version
                // wrongly read from e.NewItems and therefore always cleared to nothing.
                ResetColumnsFromColumnsSource(ColumnsSource);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(e));
        }
    }

    private void ResetColumnsFromColumnsSource(IList? newItems)
    {
        // WPF switched AutoGenerateColumns off unconditionally the moment ColumnsSource was set,
        // because every WPF page ships a DataSetListView and therefore always had columns. Here
        // the switch is driven by whether the metadata collection actually has anything in it, for
        // two real reasons: (1) a page with no list view at all would otherwise render a grid with
        // zero columns instead of the plain reflection-generated ones Phase 4's baseline showed,
        // and (2) ListPage<TEntry> fills DataSet.Columns in OnLoaded(), which runs after the view
        // is built - so an empty collection here means "not populated yet", not "no columns".
        var useAutoGeneratedColumns = newItems is not { Count: > 0 };
        var switchedToAutoGeneratedColumns = useAutoGeneratedColumns && !AutoGenerateColumns;
        AutoGenerateColumns = useAutoGeneratedColumns;

        Columns.Clear();

        if (useAutoGeneratedColumns)
        {
            // Avalonia's DataGrid only auto-generates columns while (re)binding ItemsSource - it
            // does not react to AutoGenerateColumns flipping to true afterwards (confirmed by
            // running it: the flag read back as true with zero columns). Re-assigning the same
            // ItemsSource is what actually re-runs its column generation.
            if (switchedToAutoGeneratedColumns && ItemsSource != null)
            {
                var itemsSource = ItemsSource;
                ItemsSource = null;
                ItemsSource = itemsSource;
            }

            return;
        }

        foreach (ColumnPropertyView column in newItems!)
            AddColumnFromColumnPropertyView(column);
    }

    private void AddColumnFromColumnPropertyView(ColumnPropertyView columnPropertyView)
    {
        if (columnPropertyView.IsHidden)
            return;

        var dataGridColumn = GenerateColumnFromColumnPropertyView(columnPropertyView);
        DataGridColumnExtensions.SetColumnViewModel(dataGridColumn, columnPropertyView);
        Columns.Add(dataGridColumn);
    }

    private void RemoveColumnByColumnPropertyView(ColumnPropertyView columnPropertyView)
    {
        var dataGridColumn = Columns.FirstOrDefault(
            col => ReferenceEquals(DataGridColumnExtensions.GetColumnViewModel(col), columnPropertyView));
        if (dataGridColumn != null)
            Columns.Remove(dataGridColumn);
    }

    #endregion

    #region Column generation from ColumnPropertyView metadata

    /// <summary>
    /// Applies the same Kapok column metadata to columns the DataGrid auto-generated by plain CLR
    /// reflection, i.e. when no <see cref="ColumnsSource"/> is bound. <c>AutoGeneratingColumn</c>
    /// is Avalonia's native hook for this (confirmed present in Phase 4's audit); the
    /// <see cref="ColumnPropertyView"/> built here is exactly what Kapok's own metadata layer
    /// would produce for that property, so an unconfigured page still gets localized headers,
    /// tooltips, formats and read-only handling rather than raw property names.
    /// </summary>
    private void OnAutoGeneratingColumnApplyMetadata(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        var propertyInfo = ItemsSource?.GetType().GetGenericArguments().FirstOrDefault()?
            .GetProperty(e.PropertyName);
        propertyInfo ??= ItemsSource?.Cast<object>().FirstOrDefault()?.GetType().GetProperty(e.PropertyName);
        if (propertyInfo == null)
            return;

        var columnPropertyView = new ColumnPropertyView(propertyInfo);
        if (columnPropertyView.IsHidden)
        {
            e.Cancel = true;
            return;
        }

        // Replaces the column Avalonia built rather than only tweaking it, so both paths produce
        // exactly the same column for the same property - otherwise an enum property would get a
        // Kapok enum column when it comes from metadata but a plain text column when it is
        // auto-generated. WPF's own AutoGeneratingColumnApplyControlImprovement swapped the column
        // instance for the same reason.
        var column = GenerateColumnFromColumnPropertyView(columnPropertyView);
        DataGridColumnExtensions.SetColumnViewModel(column, columnPropertyView);
        e.Column = column;
    }

    /// <summary>
    /// Creates the DataGridColumn for one <see cref="ColumnPropertyView"/>. Mirrors WPF
    /// CustomDataGrid's GenerateColumnFromColumnPropertyView + GenerateDataGridColumnExtension +
    /// AutoGeneratingColumnApplyControlImprovement chain, collapsed into a base-column choice
    /// followed by <see cref="ApplyColumnPropertyView"/>.
    /// </summary>
    protected virtual DataGridColumn GenerateColumnFromColumnPropertyView(ColumnPropertyView columnPropertyView)
    {
        Debug.Assert(columnPropertyView.PropertyInfo != null,
            $"{nameof(ColumnPropertyView)}.{nameof(ColumnPropertyView.PropertyInfo)} must be resolved before generating a column " +
            "(DataSetView.OnAddColumn sets DeclaringType for every column added to DataSet.Columns).");

        var propertyType = columnPropertyView.PropertyInfo!.PropertyType;

        var isReadOnly = columnPropertyView.IsReadOnly || IsReadOnly;

        DataGridColumn column = CreateBaseColumn(columnPropertyView, propertyType, isReadOnly);

        ApplyColumnPropertyView(ref column, columnPropertyView, isReadOnly);

        return column;
    }

    private DataGridColumn CreateBaseColumn(ColumnPropertyView columnPropertyView, Type propertyType, bool isReadOnly)
    {
        // The Kapok-specific column kinds first, in the same precedence order as WPF's
        // GenerateDataGridColumnExtension: hierarchy tree, lookup, drill-down.
        if (columnPropertyView.ShowHierarchicalTree)
            return CreateTreeColumn(columnPropertyView, isReadOnly);

        if (columnPropertyView.LookupDefinition != null &&
            LookupViews != null &&
            LookupViews.TryGetValue(columnPropertyView.Name, out var lookupView))
        {
            return CreateLookupColumn(columnPropertyView, lookupView, isReadOnly);
        }

        if (columnPropertyView.DrillDownDefinition != null &&
            DrillDownActionDictionary != null &&
            DrillDownActionDictionary.Contains(columnPropertyView.Name))
        {
            return CreateDrillDownColumn(columnPropertyView, propertyType,
                DrillDownActionDictionary[columnPropertyView.Name]);
        }

        var binding = CreateCellBinding(columnPropertyView, isReadOnly);

        // Avalonia ships only DataGridTextColumn / DataGridCheckBoxColumn / DataGridTemplateColumn
        // (confirmed by enumerating the assembly's exported types - there is no
        // DataGridComboBoxColumn and no DataGridHyperlinkColumn the way WPF has), so WPF's
        // enum -> DataGridComboBoxColumn branch becomes a template column here, and its
        // Uri -> DataGridHyperlinkColumn branch falls back to a plain text column.
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (underlyingType.IsEnum)
            return CreateEnumColumn(columnPropertyView, propertyType, isReadOnly);

        if (typeof(bool).IsAssignableFrom(underlyingType))
            return new DataGridCheckBoxColumn { Binding = binding };

        return new DataGridTextColumn { Binding = binding };
    }

    /// <summary>
    /// Hierarchy tree column (<see cref="ColumnPropertyView.ShowHierarchicalTree"/>). The row items
    /// are expected to implement <c>IHierarchyEntry&lt;TEntry&gt;</c>, whose
    /// Level/HasChildren/IsExpanded members the column binds to - the same three bindings WPF's
    /// CustomDataGrid wired up by hand.
    /// </summary>
    private DataGridColumn CreateTreeColumn(ColumnPropertyView columnPropertyView, bool isReadOnly)
    {
        var column = new DataGridTreeTextColumn
        {
            PropertyPath = BuildBindingPath(columnPropertyView),
            StringFormat = columnPropertyView.StringFormat
        };
        column.BuildTemplates(isReadOnly);
        return column;
    }

    /// <summary>
    /// Lookup column. The key path written into the row property comes from the lookup
    /// definition's own <c>FieldSelectorFunc</c> (exactly as WPF derived
    /// <c>SelectedValuePath</c>), and the display path from the lookup entry type's
    /// <c>[LookupColumn]</c> metadata.
    /// </summary>
    private DataGridColumn CreateLookupColumn(ColumnPropertyView columnPropertyView, IPropertyLookupView lookupView, bool isReadOnly)
    {
        // GetItems() (not part of the IPropertyLookupView contract - an AvaloniaPropertyLookupView
        // addition, see its doc comment) forces the lookup's lazy first query. It is passed as a
        // *provider*, not invoked here: this method runs inside ListPage<TEntry>.OnLoaded's column
        // build, while the page's own DataSet is loading, and querying a second entity from there
        // deadlocks in the shared EntityDeferredCommitService layer (hit for real - a headless run
        // that produced no output at all). See DataGridLookupComboBoxColumn.ItemsSourceProvider.
        Func<IEnumerable?> itemsProvider = () => (lookupView as Data.AvaloniaPropertyLookupView)?.GetItems();

        var selectedValuePath = GetLookupFieldName(columnPropertyView.LookupDefinition?.FieldSelectorFunc);
        if (string.IsNullOrEmpty(selectedValuePath))
        {
            // WPF logged this to Debug and carried on with an empty SelectedValuePath, producing a
            // column that silently never resolves anything. Falling back to a plain text column is
            // at least honest about showing the raw key.
            Debug.WriteLine($"ERROR: FieldSelectorFunc not set in LookupDefinition for column {columnPropertyView.Name}");
            return new DataGridTextColumn { Binding = CreateCellBinding(columnPropertyView, isReadOnly) };
        }

        var column = new DataGridLookupComboBoxColumn
        {
            PropertyPath = BuildBindingPath(columnPropertyView),
            ItemsSourceProvider = itemsProvider,
            SelectedValuePath = selectedValuePath
            // DisplayMemberPath is left unset: it is derived from the lookup entry type's
            // [LookupColumn] metadata, which needs the entries, which is exactly what must not be
            // queried yet. The column resolves it on first use.
        };
        column.BuildTemplates(isReadOnly);
        return column;
    }

    /// <summary>
    /// The property name a lookup definition's <c>FieldSelectorFunc</c> selects.
    ///
    /// Kapok.Core's own <c>Expression.GetMemberName()</c> extension - which WPF's CustomDataGrid
    /// called here - handles a member access wrapped in at most *one* conversion. The non-generic
    /// <c>ILookupDefinition.FieldSelectorFunc</c> accessor wraps the typed selector's body in
    /// another <c>Expression.Convert(..., typeof(object))</c>, so for any lookup whose field type is
    /// already a conversion (e.g. <c>taskList =&gt; taskList.Id</c> selecting a <c>Guid?</c>) the
    /// body arrives as Convert(Convert(member)) and GetMemberName throws NotSupportedException.
    /// **Found by running it**: the exception surfaced as a headless run that hung with no output
    /// at all, because it was thrown while the page was loading and ended up in the view domain's
    /// error dialog, which pushes a nested dispatcher frame nothing can close. Unwrapping every
    /// conversion layer here is the fix; the WPF original has the same latent limitation.
    /// </summary>
    private static string GetLookupFieldName(Expression<Func<object, object>>? fieldSelector)
    {
        Expression? body = fieldSelector?.Body;

        while (body is UnaryExpression unary)
            body = unary.Operand;

        return (body as MemberExpression)?.Member.Name ?? string.Empty;
    }

    /// <summary>
    /// Drill-down column: the cell text becomes a link running the DataSet's own drill-down action
    /// (an <see cref="IDataSetSelectionAction{TEntry}"/> built by
    /// <c>PropertyViewCollection.OnAdd</c>) over the grid's current selection.
    /// </summary>
    private DataGridColumn CreateDrillDownColumn(ColumnPropertyView columnPropertyView, Type propertyType, object? drillDownAction)
    {
        var column = new DataGridHyperlinkCommandColumn
        {
            PropertyPath = BuildBindingPath(columnPropertyView),
            StringFormat = columnPropertyView.StringFormat,
            AlignRight = propertyType.IsNumericType() && !(Nullable.GetUnderlyingType(propertyType) ?? propertyType).IsEnum,
            Command = drillDownAction == null ? null : CreateSelectionCommand(drillDownAction),
            // WPF bound this to CustomDataGrid.SelectedItems; here the same selection is available
            // as the bindable SelectedEntries (see item 2 - SelectedItems has no AvaloniaProperty).
            CommandParameterBinding = new Binding(nameof(SelectedEntries)) { Source = this }
        };
        column.BuildTemplates();
        return column;
    }

    /// <summary>
    /// Wraps an <c>IDataSetSelectionAction&lt;TEntry&gt;</c> as an ICommand. The closed generic is
    /// only known at runtime, so this goes through the same reflection step RibbonMenuBuilder's
    /// table-data buttons use - once per column build, not per click.
    /// </summary>
    private static ICommand? CreateSelectionCommand(object action)
    {
        var actionInterface = action.GetType().GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAction<>));

        if (actionInterface == null)
            return null;

        var forGeneric = typeof(ValueConverter.ActionCommand)
            .GetMethod(nameof(ValueConverter.ActionCommand.ForGeneric))!
            .MakeGenericMethod(actionInterface.GetGenericArguments()[0]);

        return (ICommand?)forGeneric.Invoke(null, new[] { action });
    }

    /// <summary>
    /// Enum column. WPF used a DataGridComboBoxColumn whose ElementStyle/EditingElementStyle bound
    /// ItemsSource through EnumToCollectionConverter; Avalonia has no combo box column at all, so
    /// this is a DataGridTemplateColumn showing the localized enum caption when idle and a real
    /// ComboBox over the same <see cref="EnumValueViewModel"/> list when editing.
    /// </summary>
    private DataGridColumn CreateEnumColumn(ColumnPropertyView columnPropertyView, Type propertyType, bool isReadOnly)
    {
        var propertyName = BuildBindingPath(columnPropertyView);
        var enumValues = EnumToCollectionConverter.GetListFromType(
            Nullable.GetUnderlyingType(propertyType) ?? propertyType,
            withNullable: Nullable.GetUnderlyingType(propertyType) != null);

        var displayConverter = new EnumToCollectionConverter(propertyType);

        return new DataGridTemplateColumn
        {
            IsReadOnly = isReadOnly,
            // Sorting/clipboard need a plain value path - a template column has no Binding of its
            // own, so both are told explicitly which property the cell represents.
            SortMemberPath = propertyName,
            ClipboardContentBinding = new Binding(propertyName),
            CellTemplate = new FuncDataTemplate<object>((_, _) =>
            {
                var textBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                textBlock.Bind(TextBlock.TextProperty, new Binding(propertyName)
                {
                    Converter = new EnumValueDisplayNameConverter(propertyType),
                    Mode = BindingMode.OneWay
                });
                return textBlock;
            }, supportsRecycling: true),
            CellEditingTemplate = new FuncDataTemplate<object>((_, _) =>
            {
                var comboBox = new ComboBox
                {
                    ItemsSource = enumValues,
                    SelectedValueBinding = new Binding(nameof(EnumValueViewModel.Value)),
                    DisplayMemberBinding = new Binding(nameof(EnumValueViewModel.Name)),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                comboBox.Bind(ComboBox.SelectedValueProperty, new Binding(propertyName)
                {
                    Mode = isReadOnly ? BindingMode.OneWay : BindingMode.TwoWay
                });
                return comboBox;
            }, supportsRecycling: true)
        };
    }

    /// <summary>
    /// Builds the cell binding for a bound (text/check box) column, matching WPF's binding setup:
    /// the property path (plus <see cref="PropertyView.ArrayIndex"/> if set), one-way for
    /// read-only columns, and LostFocus as the update trigger so a half-typed value never reaches
    /// the entity.
    /// </summary>
    private static Binding CreateCellBinding(ColumnPropertyView columnPropertyView, bool isReadOnly)
    {
        var binding = new Binding(BuildBindingPath(columnPropertyView))
        {
            Mode = isReadOnly ? BindingMode.OneWay : BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
        };

        if (columnPropertyView.StringFormat != null)
            binding.StringFormat = columnPropertyView.StringFormat;
        if (columnPropertyView.NullDisplayText != null)
            binding.TargetNullValue = columnPropertyView.NullDisplayText;

        return binding;
    }

    private static string BuildBindingPath(ColumnPropertyView columnPropertyView)
    {
        var path = columnPropertyView.Name;
        if (columnPropertyView.ArrayIndex.HasValue)
            path += $"[{columnPropertyView.ArrayIndex.Value}]";
        return path;
    }

    /// <summary>
    /// Applies everything a <see cref="ColumnPropertyView"/> says about a column that is
    /// independent of which column type was chosen: read-only state, width, cell style classes,
    /// header caption/tooltip and filterability.
    /// </summary>
    protected virtual void ApplyColumnPropertyView(ref DataGridColumn column, ColumnPropertyView columnPropertyView, bool isReadOnly)
    {
        var propertyType = columnPropertyView.PropertyInfo?.PropertyType;

        column.IsReadOnly = isReadOnly;

        column.Width = columnPropertyView.Width.HasValue
            ? new DataGridLength(columnPropertyView.Width.Value)
            : DataGridLength.Auto;

        // A lookup column's cell shows the referenced entry's *display* text, not the key, so the
        // key's own type must not drive the cell styling (a Guid key was getting the
        // ellipsis-trimming class meant for raw Guids).
        if (propertyType != null && column is not DataGridLookupComboBoxColumn)
        {
            var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            // IsNumericType() goes through Type.GetTypeCode, which reports an enum as its
            // *underlying* integral type - so a plain IsNumericType() check right-aligns enum
            // columns too. Confirmed the hard way (Task.Priority came out with the numeric cell
            // class); enums are excluded explicitly.
            if (!underlyingType.IsEnum && propertyType.IsNumericType())
                column.CellStyleClasses.Add(NumericCellClass);
            else if (underlyingType == typeof(Guid))
                column.CellStyleClasses.Add(GuidCellClass);
        }

        if (columnPropertyView.TextWrap)
            column.CellStyleClasses.Add(TextWrapCellClass);

        if (!columnPropertyView.IsFilterable)
            DataGridColumnExtensions.SetCanUserFilter(column, false);

        ApplyColumnHeader(column, columnPropertyView);
    }

    private void ApplyColumnHeader(DataGridColumn column, ColumnPropertyView columnPropertyView)
    {
        var culture = CultureInfo.CurrentUICulture;

        // WPF preferred the short name whenever one exists, with a TODO to show the long name when
        // the header is wide enough - same preference kept here, and the long name is what the
        // tooltip shows, which is that TODO's practical answer.
        var header = columnPropertyView.DisplayShortName?.LanguageOrDefault(culture)
                     ?? columnPropertyView.DisplayName?.LanguageOrDefault(culture)
                     ?? columnPropertyView.Name;
        column.Header = header;

        var description = columnPropertyView.DisplayDescription?.LanguageOrDefault(culture);
        var tooltipHeaderText = columnPropertyView.DisplayName?.LanguageOrDefault(culture) ?? header;
        var hasTooltip = columnPropertyView.DisplayShortName != null || columnPropertyView.DisplayDescription != null;

        // WPF built the tooltip as a single StackPanel instance and parked it on an attached
        // property its column-header style then read. A single control instance cannot be reused
        // across regenerated headers in Avalonia (one visual parent per control), so the tooltip
        // is created fresh per header from a HeaderTemplate instead; the attached property is
        // still set, so anything else can read it back.
        if (hasTooltip)
            DataGridColumnExtensions.SetHeaderTooltip(column, BuildHeaderTooltip(tooltipHeaderText, description));

        var canUserFilter = DataGridColumnExtensions.GetCanUserFilter(column);
        var propertyName = columnPropertyView.Name;

        // The header is caption + per-column filter input, stacked - the same two-row header WPF
        // achieved by replacing DataGridColumnHeader's entire ControlTemplate. Avalonia renders
        // HeaderTemplate inside the stock header, so the real Fluent header chrome (resize
        // grippers, sort indicator, hover states) stays untouched.
        column.HeaderTemplate = new FuncDataTemplate<object>((value, _) =>
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };

            var textBlock = new TextBlock
            {
                Text = value?.ToString(),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            if (hasTooltip)
                ToolTip.SetTip(textBlock, BuildHeaderTooltip(tooltipHeaderText, description));
            panel.Children.Add(textBlock);

            var filterControl = new DataGridColumnFilter { CanUserFilter = canUserFilter };
            filterControl.Bind(IsVisibleProperty, new Binding(nameof(IsFilterVisible)) { Source = this });

            // The view model is created when the header is actually realized, not here: the grid's
            // Filter is bound by its host after the columns are generated (see ListPageView), and
            // a column can also be regenerated when the page's current list view changes.
            filterControl.AttachedToVisualTree += (_, _) =>
                filterControl.ColumnFilter ??= CreateColumnFilterViewModel(propertyName);
            filterControl.DetachedFromVisualTree += (_, _) =>
            {
                filterControl.ColumnFilter?.Detach();
                filterControl.ColumnFilter = null;
            };

            panel.Children.Add(filterControl);
            return panel;
        }, supportsRecycling: false);
    }

    private static Control BuildHeaderTooltip(string headerText, string? description)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = headerText,
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 5)
        });

        if (description != null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 250
            });
        }

        return panel;
    }

    #endregion
}

/// <summary>
/// Resolves one enum value to its localized <see cref="EnumValueViewModel.Name"/>. WPF got this
/// for free from DataGridComboBoxColumn's DisplayMemberPath; Avalonia's enum column is a template
/// column showing a TextBlock, which needs the value-to-caption step done explicitly.
/// </summary>
internal sealed class EnumValueDisplayNameConverter : global::Avalonia.Data.Converters.IValueConverter
{
    private readonly Dictionary<object, string> _namesByValue;

    public EnumValueDisplayNameConverter(Type propertyType)
    {
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        _namesByValue = EnumToCollectionConverter.GetListFromType(underlyingType, withNullable: false)
            .Where(v => v.Value != null)
            .ToDictionary(v => v.Value!, v => v.Name);
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null && _namesByValue.TryGetValue(value, out var name) ? name : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
