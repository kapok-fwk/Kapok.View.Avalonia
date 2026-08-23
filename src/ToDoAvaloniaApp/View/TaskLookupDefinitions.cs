using Kapok.Entity.Model;
using ToDoAvaloniaApp.DataModel;
using Task = ToDoAvaloniaApp.DataModel.Task;

namespace ToDoAvaloniaApp.View;

/// <summary>
/// Lookup definitions shared between the pages that reference a <see cref="TaskList"/>.
///
/// Phase 5 declared this inline in <see cref="TaskCard"/> for the card-page LookupComboBox. Phase 7
/// item 4 added the same lookup as a *grid column* on the Tasks list page, and both have to describe
/// the identical relationship (same entries, same key), so it lives in one place now.
/// </summary>
public static class TaskLookupDefinitions
{
    /// <summary>
    /// The TaskList a Task belongs to. The entries do not depend on the current Task (any TaskList
    /// is a valid choice), so the entry-independent overload is used - matching
    /// <see cref="ILookupDefinition.EntriesFuncDependentOnEntry"/> being false.
    ///
    /// A fresh instance per call, deliberately: a LookupDefinition is turned into an
    /// <c>IPropertyLookupView</c> per DataSet (see PropertyViewCollection.OnAdd), each of which
    /// queries through its own data-domain scope and caches its own entries.
    /// </summary>
    public static LookupDefinition<Task, TaskList, Guid?> TaskListLookup() =>
        new(scope => scope.GetEntityService<TaskList>().AsQueryable().ToList(),
            taskList => taskList.Id);
}
