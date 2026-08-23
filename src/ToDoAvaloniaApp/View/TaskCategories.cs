using Kapok.View;
using ToDoAvaloniaApp.DataModel;

namespace ToDoAvaloniaApp.View;

/// <summary>
/// The showcase page for Phase 7 item 4's hierarchy tree column: a flat
/// <see cref="ListPage{TEntry}"/> over <see cref="TaskCategory"/> whose Name column is marked
/// <see cref="ColumnPropertyView.ShowHierarchicalTree"/>, so CustomDataGrid generates a
/// <c>DataGridTreeTextColumn</c> for it.
///
/// **Deliberately a plain ListPage, not a HierarchyListPage.** Kapok's hierarchy *navigation* -
/// expanding a node re-querying its children, move-in/move-out - lives in
/// <c>IHierarchyDataSetView&lt;TEntry&gt;</c>, whose only implementation is WPF's 600-line
/// <c>HierarchyDataSetView</c> built entirely on WPF's ICollectionView; this port's
/// <c>AvaloniaViewDomain.CreateHierarchyDataSetView</c> still throws NotSupportedException and
/// porting it is its own workstream (see the porting plan). The *column* does not need any of
/// that: it renders indentation, connector lines and an expander from each row's own
/// Level/HasChildren/IsExpanded values, which is exactly what this page provides. So this verifies
/// the column honestly, and the navigation gap stays visible rather than being glossed over.
/// </summary>
public class TaskCategories : ListPage<TaskCategory>
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
}
