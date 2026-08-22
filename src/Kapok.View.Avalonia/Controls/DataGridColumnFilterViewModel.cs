using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Kapok.BusinessLayer;
using Kapok.View.Avalonia.Localization;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// View model behind one column's filter input. Port of Kapok.View.Wpf's
/// DataGridColumnFilterViewModel - the logic is entirely Kapok.Core filter plumbing
/// (<see cref="IPropertyFilterCollection"/> / <see cref="PropertyFilterStringFilter"/>) with no
/// WPF API in it, so this is a near-line-for-line port.
///
/// Two intentional differences from the WPF original:
///
///  - It takes the <see cref="IPropertyFilterCollection"/> directly instead of a
///    <c>CustomDataGrid</c>. WPF's version reached back through the control purely to read
///    <c>dataGrid.Filter</c>; taking the collection makes the class testable and control-free,
///    and it is the only thing it ever needed.
///  - The WPF original exposed an <c>ICommand UpdateFilterCommand</c> because its ControlTemplate
///    could only invoke behaviour through a command binding. Here the control calls
///    <see cref="UpdateFilter"/> directly (it is built in code, not XAML), so no command wrapper
///    is needed. The method is public and does exactly the same thing.
///
/// Not ported: Kapok.View.Wpf's <c>FilterType.cs</c> (<c>DataGridColumnFilterType</c>, a
/// Text/List enum). It has zero consumers anywhere in the WPF source - grep-confirmed, its own
/// doc comment points at a "Generic.xaml" that does not exist in the repository - i.e. it is dead
/// code there, the same finding Phase 5 made about <c>FlatComboBoxStyle.xaml</c>.
/// </summary>
public class DataGridColumnFilterViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
{
    private const string ResxName = "Kapok.View.Avalonia.Resources.Controls.DataGridColumnFilterViewModel";

    private readonly IPropertyFilterCollection _filter;
    private readonly Type _elementType;
    private bool _isReadOnly;
    private string _queryString = string.Empty;

    public string PropertyBindingPath { get; }

    public DataGridColumnFilterViewModel(IPropertyFilterCollection filter, Type elementType, string propertyPath)
    {
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _elementType = elementType;
        PropertyBindingPath = propertyPath;

        SetQueryStringFromProperty();

        if (_filter.Properties is INotifyCollectionChanged observableCollection)
        {
            // When a filter was already set programmatically, run the same subscribe path now so
            // this view model reflects it (and follows its changes) from the start.
            var propertyFilter = PropertyFilter;
            if (propertyFilter != null)
            {
                Filter_CollectionChanged(null, new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add, new[] { propertyFilter }.ToList()));
            }

            // WPF needed CollectionChangedEventManager (a weak-event wrapper) here. Avalonia has
            // no event-manager layer; the subscription is released in Detach(), which the control
            // calls when its header is torn down.
            observableCollection.CollectionChanged += Filter_CollectionChanged;
            _observedCollection = observableCollection;
        }
    }

    private readonly INotifyCollectionChanged? _observedCollection;

    /// <summary>
    /// Releases the collection subscription. WPF relied on its weak event manager for this; a
    /// plain CLR event needs an explicit unsubscribe when the column header goes away.
    /// </summary>
    public void Detach()
    {
        if (_observedCollection != null)
            _observedCollection.CollectionChanged -= Filter_CollectionChanged;

        if (PropertyFilter is ValidatableBindableObjectBase current)
        {
            current.PropertyChanged -= PropertyFilter_PropertyChanged;
            current.ErrorsChanged -= PropertyFilter_ErrorsChanged;
        }
    }

    private void Filter_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        bool addFilter;
        bool removeFilter;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Reset:
                SetQueryStringInternal(string.Empty);
                return;
            case NotifyCollectionChangedAction.Add:
                addFilter = true;
                removeFilter = false;
                break;
            case NotifyCollectionChangedAction.Remove:
                addFilter = false;
                removeFilter = true;
                break;
            case NotifyCollectionChangedAction.Replace:
                addFilter = true;
                removeFilter = true;
                break;
            default:
                return;
        }

        // WPF gated *everything* below on the filter also being an IPropertyFilterStringFilter,
        // which meant a filter of any other kind (e.g. a PropertyStaticFilter set programmatically)
        // was added to the user layer without the input ever redisplaying it - reproduced here
        // before changing it: the box stayed empty while the filter was genuinely active, which is
        // worse than showing a value the user cannot edit. The type check now only guards the
        // event *subscription*; refreshing what is displayed happens for every filter type, which
        // is what SetQueryStringFromProperty's own non-string-filter branch (AsFilterString /
        // read-only) exists for in the first place.
        var changedFilter = removeFilter
            ? e.OldItems?.Cast<IPropertyFilter>().FirstOrDefault(pf => pf.PropertyInfo.Name == PropertyBindingPath)
            : null;

        if (changedFilter is ValidatableBindableObjectBase removeItem)
        {
            removeItem.PropertyChanged -= PropertyFilter_PropertyChanged;
            removeItem.ErrorsChanged -= PropertyFilter_ErrorsChanged;
        }

        var newFilter = addFilter
            ? e.NewItems?.Cast<IPropertyFilter>().FirstOrDefault(pf => pf.PropertyInfo.Name == PropertyBindingPath)
            : null;

        if (newFilter is ValidatableBindableObjectBase addItem)
        {
            addItem.PropertyChanged += PropertyFilter_PropertyChanged;
            addItem.ErrorsChanged += PropertyFilter_ErrorsChanged;
        }

        if (changedFilter != null || newFilter != null)
            SetQueryStringFromProperty();
    }

    private IPropertyFilter? PropertyFilter =>
        _filter.Properties?.FirstOrDefault(d => d.PropertyInfo.Name == PropertyBindingPath);

    /// <summary>
    /// Identifies if the query string is read only (= can not be changed). This is the case when
    /// the column already carries a filter that cannot be round-tripped through a filter string
    /// (see <see cref="PropertyFilterExtension.AsFilterString"/> returning null).
    /// </summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (_isReadOnly == value) return;
            _isReadOnly = value;
            OnPropertyChanged();
        }
    }

    private void SetQueryStringFromProperty()
    {
        // The property filter object this view model reports errors from is about to change (it
        // was just added to, or removed from, the user filter layer), so whatever validation state
        // the UI is showing is stale. WPF never did this and its error indicator therefore stuck
        // after a bad filter was cleared - reproduced here first (the red ":error" state survived
        // clearing the input), which is why the notification is raised explicitly.
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(QueryString)));

        if (PropertyFilter == null)
        {
            SetQueryStringInternal(string.Empty);
            IsReadOnly = false;
            return;
        }

        if (PropertyFilter is IPropertyFilterStringFilter propertyFilterStringFilter)
        {
            SetQueryStringInternal(propertyFilterStringFilter.FilterString ?? string.Empty);
            IsReadOnly = false;
            return;
        }

        var filterString = PropertyFilter.AsFilterString();

        if (filterString == null)
        {
            SetQueryStringInternal(ResxManager.GetString(ResxName, "FilterStringNotAvailableText"));
            IsReadOnly = true;
        }
        else
        {
            SetQueryStringInternal(filterString);
            IsReadOnly = false;
        }
    }

    private void SetQueryStringInternal(string value)
    {
        if (_queryString == value) return;
        _queryString = value;
        OnPropertyChanged(nameof(QueryString));
    }

    [Required]
    public string QueryString
    {
        get => _queryString;
        set
        {
            if (IsReadOnly)
                throw new NotSupportedException($"The property {nameof(QueryString)} cannot be changed. The query is read-only.");

            SetQueryStringInternal(value);
        }
    }

    /// <summary>
    /// Applies <see cref="QueryString"/> to the DataSet's user filter layer: adds, updates or
    /// removes this column's <see cref="PropertyFilterStringFilter"/>. Direct port of WPF's
    /// UpdateFilter (which it reached through an ICommand).
    /// </summary>
    public void UpdateFilter()
    {
        var propertyFilter = PropertyFilter;
        if (propertyFilter == null)
        {
            if (string.IsNullOrEmpty(QueryString))
            {
                // Skip when filter text is empty
                return;
            }

            _filter.Properties.Add(CreateStringFilter(QueryString));
        }
        else if (string.IsNullOrWhiteSpace(QueryString))
        {
            _filter.Properties.Remove(propertyFilter);
        }
        else if (propertyFilter is IPropertyFilterStringFilter propertyFilterStringFilter)
        {
            propertyFilterStringFilter.FilterString = QueryString;
        }
        else
        {
            // The existing filter is not a string filter (e.g. a PropertyStaticFilter set from
            // application code) - replace it with an equivalent string filter carrying what the
            // user typed.
            _filter.ReplacePropertyFilter(propertyFilter, CreateStringFilter(QueryString));
        }
    }

    /// <summary>
    /// Creates the closed-generic <c>PropertyFilterStringFilter&lt;TEntry&gt;</c> for this column.
    ///
    /// The closed generic matters: <c>PropertyFilterCollection&lt;T&gt;.ReplacePropertyFilter</c>
    /// casts both filters to <c>IPropertyFilter&lt;T&gt;</c>, which the non-generic
    /// <c>PropertyFilterStringFilter</c> does not implement. WPF's version used the non-generic
    /// class when *adding* a filter and only the generic one when replacing, so a filter the user
    /// had typed could never afterwards be replaced - an InvalidCastException waiting to happen,
    /// confirmed here by actually hitting it from the other side (replacing a non-generic filter).
    /// Both paths use the generic form.
    /// </summary>
    private IPropertyFilter CreateStringFilter(string filterString)
    {
        var filterType = typeof(PropertyFilterStringFilter<>).MakeGenericType(_elementType);
        var filter = (IPropertyFilterStringFilter)Activator.CreateInstance(filterType, PropertyBindingPath)!;
        filter.FilterString = filterString;
        return (IPropertyFilter)filter;
    }

    private void PropertyFilter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPropertyFilterStringFilter.FilterString) &&
            PropertyFilter is IPropertyFilterStringFilter stringFilter)
        {
            SetQueryStringInternal(stringFilter.FilterString ?? string.Empty);
        }
    }

    private void PropertyFilter_ErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPropertyFilterStringFilter.FilterString))
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(QueryString)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region INotifyDataErrorInfo

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName == nameof(QueryString) && PropertyFilter is INotifyDataErrorInfo errorInfo)
            return errorInfo.GetErrors(nameof(IPropertyFilterStringFilter.FilterString)) ?? Array.Empty<object>();

        return Array.Empty<object>();
    }

    public bool HasErrors => (PropertyFilter as INotifyDataErrorInfo)?.HasErrors ?? false;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    #endregion
}
