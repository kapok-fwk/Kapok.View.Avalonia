using System.Collections;
using Avalonia.Controls;

namespace Kapok.View.Avalonia.DefaultPageControls;

/// <summary>
/// Generic IListPage control. Matches Kapok.View.Wpf's ListPageControl.xaml, minus the real
/// DataGrid (CustomDataGrid's dynamic-columns-from-metadata/filtering/etc. is its own later
/// phase, see the porting plan) - a plain ListBox stands in here so the ViewDomain/DataSetView
/// binding pipeline can be proven end to end without waiting on that. WPF's ListPageControl.xaml
/// also has its own small toolbar here (sort-ascending/descending, list-view selector, filter
/// toggle) - those are DataSet-level, grid-specific controls (DataSet.SortAscendingAction etc.),
/// not the page's Base menu, so they belong with the real DataGrid work later, not this stand-in.
/// Phase 1/2 note: this used to also embed a MenuToolbar rendering the page's Base menu, before
/// PageWindow had a real Ribbon to show it - removed once the Ribbon (Windows/PageWindow.cs) took
/// over that job, so the same actions don't render twice.
///
/// DataSet.Collection is exposed only via the closed generic IDataSetReadonlyView&lt;TEntry&gt;
/// interface (DataSetView&lt;TEntry&gt; implements it as an explicit interface member on a protected
/// field), and this control is shared across every entity type, so TEntry isn't known at compile
/// time here. Resolved via one reflection lookup per page instead - it returns the actual live
/// ObservableCollection&lt;TEntry&gt; instance (not a copy), so the ListBox still gets real
/// live updates through Avalonia's normal INotifyCollectionChanged binding support.
/// </summary>
public class ListPageView : UserControl
{
    private readonly ListBox _listBox;

    public ListPageView()
    {
        _listBox = new ListBox();
        Content = _listBox;

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
            _listBox.ItemsSource = collection;
    }
}
