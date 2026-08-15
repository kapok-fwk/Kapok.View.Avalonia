using System.Collections.ObjectModel;
using Kapok.Data;
using Kapok.Entity.Model;

namespace Kapok.View.Avalonia.Data;

/// <summary>
/// Avalonia equivalent of Kapok.View.Wpf's PropertyLookupView. Holds the possible options to show
/// in a UI combobox. Exposes a plain ObservableCollection rather than WPF's
/// ICollectionView/CollectionViewSource, matching Avalonia's binding model.
/// </summary>
public class AvaloniaPropertyLookupView : IPropertyLookupView
{
    private readonly Func<object?>? _currentSelector;
    private readonly IDataDomain _dataDomain;
    private bool _isRefreshedOnce;

    public AvaloniaPropertyLookupView(ILookupDefinition lookupDefinition, IDataDomain dataDomain,
        Func<object?>? currentSelector = null)
    {
        LookupDefinition = lookupDefinition;
        _dataDomain = dataDomain;
        _currentSelector = currentSelector;
    }

    public ILookupDefinition LookupDefinition { get; }

    public ObservableCollection<object> Items { get; } = new();

    /// <summary>
    /// Enforces a refresh on first access, matching PropertyLookupView.View's lazy-refresh behavior.
    /// </summary>
    public ObservableCollection<object> GetItems()
    {
        if (!_isRefreshedOnce)
            Refresh();
        return Items;
    }

    public void Refresh()
    {
        Items.Clear();

        IEnumerable<object>? newItems;
        if (LookupDefinition.EntriesFuncDependentOnEntry)
        {
            var current = _currentSelector?.Invoke();
            if (current == null)
            {
                newItems = null;
            }
            else
            {
                using var scope = _dataDomain.CreateScope();
                newItems = LookupDefinition.EntriesFunc.Invoke(current, scope);
            }
        }
        else
        {
            using var scope = _dataDomain.CreateScope();
            newItems = LookupDefinition.EntriesFunc.Invoke(null, scope);
        }

        if (newItems != null)
        {
            foreach (var item in newItems)
                Items.Add(item);
        }

        _isRefreshedOnce = true;
    }
}
