using System.Collections;
using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Kapok.Entity;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// A grid column whose cell editor is a <see cref="LookupComboBox"/> - a combo box whose dropdown
/// is a multi-column data grid of the lookup entries - and whose read-only cell shows the *display*
/// value of the currently referenced entry rather than the raw key.
///
/// Port of Kapok.View.Wpf's DataGridLookupComboBoxColumn onto a <see cref="DataGridTemplateColumn"/>
/// (the extensibility point Avalonia actually offers - it ships no DataGridComboBoxColumn at all,
/// confirmed by enumerating the assembly's exported column types).
///
/// Three deliberate differences from the WPF original, each because the WPF behaviour is either
/// impossible or wrong here:
///
///  1. **The read-only cell resolves the display value through the lookup list.** WPF bound the
///     cell's TextBlock to <c>"{SelectedValuePath}.{DisplayMemberPath}"</c> *on the row entity* -
///     i.e. it assumed the entity exposes a navigation property at the key's own path, which is
///     only true for some models (for a plain <c>Guid? TaskListId</c> it produces nothing). This
///     column instead looks the entry up in the lookup items by
///     <see cref="SelectedValuePath"/> and shows its <see cref="DisplayMemberPath"/> - the same
///     value the editor shows, which is what a user expects a lookup cell to display.
///  2. **The column is generated even when it is read-only.** WPF only produced a lookup column
///     for an editable, string-typed property and otherwise fell back to a plain text column - so a
///     read-only lookup column showed the raw key. Since point 1 makes the display case work, the
///     restriction serves no purpose; a read-only column simply gets no editing template.
///  3. **No <c>PauseExcelNavigation</c> binding on IsDropDownOpen.** That WPF binding belongs to
///     the Excel-navigation feature, which is a separate (still open) item - see the porting plan.
/// </summary>
public class DataGridLookupComboBoxColumn : DataGridTemplateColumn
{
    /// <summary>The property path, on a lookup entry, holding the value written to the cell.</summary>
    public string SelectedValuePath { get; set; } = string.Empty;

    /// <summary>The property path, on a lookup entry, shown to the user.</summary>
    public string DisplayMemberPath { get; set; } = string.Empty;

    /// <summary>The property path, on the row entity, the cell reads/writes.</summary>
    public string PropertyPath { get; set; } = string.Empty;

    /// <summary>
    /// Resolves the lookup entries (from <c>DataSet.Columns.LookupViews[property].GetItems()</c>).
    ///
    /// A provider, not a plain collection: <c>GetItems()</c> runs the lookup's first real query, and
    /// column generation happens inside <c>ListPage&lt;TEntry&gt;.OnLoaded</c> while the page's own
    /// DataSet is still loading. Querying a second entity from there deadlocks in the shared
    /// EntityDeferredCommitService layer - the same hang Phase 5 documented and this item hit again
    /// (a headless run that never produced a single line of output). Resolving on first cell render
    /// instead keeps the query out of the load path entirely, and is better behaviour regardless:
    /// a lookup column that is never shown never queries.
    /// </summary>
    public Func<IEnumerable?>? ItemsSourceProvider { get; set; }

    /// <summary>
    /// Called with each editor as it is created, so the owning grid can hook it up (it is used to
    /// pause Excel navigation while the dropdown is open). A callback rather than a back-reference
    /// to the grid, so the column stays independent of it.
    /// </summary>
    public Action<ComboBox>? OnEditorCreated { get; set; }

    /// <summary>
    /// Builds both templates. Called once the properties above are set, since a
    /// DataGridTemplateColumn's templates are plain values, not virtual members.
    /// </summary>
    public void BuildTemplates(bool isReadOnly)
    {
        var propertyPath = PropertyPath;
        var displayConverter = new LookupDisplayConverter(ItemsSourceProvider, SelectedValuePath, DisplayMemberPath);

        // Sorting and clipboard content follow the underlying key, matching WPF's
        // ClipboardContentBinding falling back to SelectedValueBinding.
        SortMemberPath = propertyPath;
        ClipboardContentBinding = new Binding(propertyPath);
        IsReadOnly = isReadOnly;

        CellTemplate = new FuncDataTemplate<object>((_, _) =>
        {
            var textBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            textBlock.Bind(TextBlock.TextProperty, new Binding(propertyPath)
            {
                Converter = displayConverter,
                Mode = BindingMode.OneWay
            });
            return textBlock;
        }, supportsRecycling: true);

        if (isReadOnly)
            return;

        CellEditingTemplate = new FuncDataTemplate<object>((_, _) =>
        {
            var items = ItemsSourceProvider?.Invoke();
            var comboBox = new LookupComboBox
            {
                ItemsSource = items,
                // SelectedValueBinding is evaluated against each *lookup entry* - it is what turns
                // a selected entry into the value written back to the row's property.
                SelectedValueBinding = new Binding(SelectedValuePath),
                DisplayMemberBinding = new Binding(ResolveDisplayMemberPath(items, SelectedValuePath)),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            comboBox.Bind(ComboBox.SelectedValueProperty, new Binding(propertyPath) { Mode = BindingMode.TwoWay });
            OnEditorCreated?.Invoke(comboBox);
            return comboBox;
        }, supportsRecycling: false);
    }

    /// <summary>
    /// Resolves the display path for a lookup entry type: the first property carrying
    /// <c>[LookupColumn(Show = true)]</c>, which is the same metadata
    /// <see cref="LookupComboBox"/>'s dropdown grid uses to decide which columns to show, falling
    /// back to the key path itself.
    /// </summary>
    internal static string ResolveDisplayMemberPath(IEnumerable? items, string selectedValuePath)
    {
        var itemType = items?.Cast<object>().FirstOrDefault()?.GetType();
        if (itemType == null)
            return selectedValuePath;

        var displayProperty = itemType.GetProperties()
            .FirstOrDefault(p => p.GetCustomAttribute<LookupColumnAttribute>() is { Show: true });

        return displayProperty?.Name ?? selectedValuePath;
    }
}

/// <summary>
/// Maps a lookup key value to the display text of the matching lookup entry.
/// </summary>
internal sealed class LookupDisplayConverter : IValueConverter
{
    private readonly Func<IEnumerable?>? _itemsProvider;
    private readonly string _selectedValuePath;
    private readonly string _displayMemberPathOverride;
    private IEnumerable? _items;
    private string? _displayMemberPath;

    public LookupDisplayConverter(Func<IEnumerable?>? itemsProvider, string selectedValuePath, string displayMemberPathOverride)
    {
        _itemsProvider = itemsProvider;
        _selectedValuePath = selectedValuePath;
        _displayMemberPathOverride = displayMemberPathOverride;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || string.IsNullOrEmpty(_selectedValuePath))
            return null;

        // First real cell render is what triggers the lookup query - see ItemsSourceProvider. Not
        // cached once it is non-empty only: the provider itself retries an empty result, and
        // caching an empty list here would defeat that.
        if (_items == null || !_items.Cast<object>().Any())
            _items = _itemsProvider?.Invoke();

        if (_items == null)
            return value.ToString();

        _displayMemberPath ??= string.IsNullOrEmpty(_displayMemberPathOverride)
            ? DataGridLookupComboBoxColumn.ResolveDisplayMemberPath(_items, _selectedValuePath)
            : _displayMemberPathOverride;

        foreach (var item in _items)
        {
            var itemType = item?.GetType();
            var key = itemType?.GetProperty(_selectedValuePath)?.GetValue(item);
            if (key == null || !key.Equals(value))
                continue;

            return itemType?.GetProperty(_displayMemberPath)?.GetValue(item)?.ToString();
        }

        // No match: show the raw key rather than an empty cell, so a dangling reference is visible
        // instead of silently looking like "not set".
        return value.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
