using System.Collections;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Kapok.View.Avalonia.DefaultPageControls;

/// <summary>
/// Generic IListPage control. Matches Kapok.View.Wpf's ListPageControl.xaml, minus the real
/// DataGrid (CustomDataGrid's dynamic-columns-from-metadata/filtering/etc. is its own later
/// phase, see the porting plan) - a plain ListBox stands in here so the ViewDomain/DataSetView
/// binding pipeline can be proven end to end without waiting on that.
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

        Content = new DockPanel
        {
            Children =
            {
                new MenuToolbar { [DockPanel.DockProperty] = Dock.Top },
                _listBox
            }
        };

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
