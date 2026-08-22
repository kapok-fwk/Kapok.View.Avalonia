using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Kapok.BusinessLayer;
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
    }

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

        if (propertyType != null)
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
