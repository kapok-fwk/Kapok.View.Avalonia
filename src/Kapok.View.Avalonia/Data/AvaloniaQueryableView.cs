using System.Collections.ObjectModel;

namespace Kapok.View.Avalonia.Data;

/// <summary>
/// Avalonia equivalent of Kapok.View.Wpf's QueryableCollectionViewSource&lt;T&gt;. Avalonia has no
/// ICollectionView/CollectionViewSource equivalent, so this just exposes a plain
/// ObservableCollection - which is exactly what Avalonia's own binding/ItemsSource system expects.
/// </summary>
public class AvaloniaQueryableView<T> : IQueryableView<T>
    where T : class
{
    public ObservableCollection<T> Items { get; } = new();

    private IQueryable<T>? _queryableSource;
    public IQueryable<T>? QueryableSource
    {
        get => _queryableSource;
        set
        {
            if (_queryableSource == value) return;
            _queryableSource = value;
            Refresh();
        }
    }

    IQueryable<T> IQueryableView<T>.QueryableSource => QueryableSource!;

    public void Refresh()
    {
        Items.Clear();
        if (QueryableSource == null) return;

        // WPF's QueryableCollectionViewSource<T> calls EF Core's AsNoTracking() here as a
        // performance hint. This project deliberately has no direct EF Core dependency (see the
        // porting plan), so that's left to the caller's IQueryable (e.g. an EF Core
        // IDataDomainScope-backed queryable can already be built untracked upstream) rather than
        // referencing EF Core here just for this one call.
        foreach (var item in QueryableSource)
            Items.Add(item);
    }
}
