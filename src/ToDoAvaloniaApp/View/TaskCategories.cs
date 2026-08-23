using Kapok.View;
using ToDoAvaloniaApp.DataModel;

namespace ToDoAvaloniaApp.View;

/// <summary>
/// The showcase page for both Phase 7 item 4's hierarchy tree column (indentation, connector
/// lines and an expander rendered from each row's own Level/HasChildren/IsExpanded) and Phase 8
/// item 4's hierarchy *navigation* - expanding a node, moving it in/out a level.
///
/// A real <see cref="HierarchyListPage{TEntry}"/> (not plain <see cref="ListPage{TEntry}"/>, as
/// this page was through Phase 7 - <c>AvaloniaViewDomain.CreateHierarchyDataSetView</c> threw
/// NotSupportedException until Phase 8 item 4), which is what supplies the MoveIn/MoveOut menu
/// actions. <see cref="InitializeBaseDataSet"/> is overridden to route through
/// <c>CreateHierarchyDataSetView</c> instead of the base class's own <c>CreateDataSetView</c> -
/// core Kapok.View's own <c>HierarchyListPage&lt;TEntry&gt;</c> never does this itself (confirmed
/// by reading it: it only adds the two menu actions, referencing <c>DataSet as
/// IHierarchyDataSetView&lt;TEntry&gt;</c> without ever arranging for <c>DataSet</c> to actually be
/// one), and neither does <c>Kapok.View.Wpf.WpfViewDomain.CreateDataSetView</c> auto-detect
/// <c>IHierarchyEntry&lt;TEntry&gt;</c> - so on both platforms, a page that only extends
/// <c>HierarchyListPage&lt;TEntry&gt;</c> without also overriding <c>InitializeBaseDataSet</c>
/// would get a plain, non-hierarchical DataSetView and its Move* actions would always report
/// <c>CanExecute() == false</c>. A real (if narrow) gap in the shared core/WPF design, found while
/// wiring this page up - out of scope to fix in either of those repos from here, so this override
/// is the fix at the one call site this port actually has.
/// </summary>
public class TaskCategories : HierarchyListPage<TaskCategory>
{
    public TaskCategories(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        Title = "Task categories";

        ListViews.Add(new DataSetListView
        {
            Name = "Standard",
            Columns = new List<ColumnPropertyView>
            {
                new(nameof(TaskCategory.Name)) { ShowHierarchicalTree = true, Width = 260 },
                new(nameof(TaskCategory.Level)) { Width = 90 }
            }
        });
    }

    protected override IDataSetView<TaskCategory> InitializeBaseDataSet()
    {
        var dataSetView = ViewDomain.CreateHierarchyDataSetView<TaskCategory>(DataDomainScope);
        dataSetView.InsertAllowed = AllowCreateNewEntry && (Editable || OpenCardPageAction != null);
        dataSetView.ModifyAllowed = Editable;
        dataSetView.DeleteAllowed = AllowDeleteEntry && (Editable || OpenCardPageAction != null);
        return dataSetView;
    }
}
