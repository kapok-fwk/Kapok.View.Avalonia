using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Kapok.BusinessLayer;
using Kapok.Data;
using Kapok.Entity;

namespace Kapok.View.Avalonia.Data;

/// <summary>
/// Hierarchy *navigation* (expand/collapse a node, move a node in/out a level) - as opposed to
/// Phase 7 item 4's tree *column*, which only ever renders whatever Level/HasChildren/IsExpanded
/// already say. Rewrite, not a port: Kapok.View.Wpf's HierarchyDataSetView (~600 lines) is built
/// entirely on WPF's <c>ICollectionView.CurrentItem</c>/<c>MoveCurrentToPrevious</c>/
/// <c>MoveCurrentToNext</c>, and the core, framework-agnostic <see cref="DataSetView{TEntry}"/> this
/// module builds on (like WPF's own base class) has no such concept at all - <c>Collection</c> is a
/// plain <see cref="ObservableCollection{T}"/> that this port's <c>CustomDataGrid</c> binds to
/// directly, so it already *is* the real display order, with no proxy view sitting in front of it.
/// Every place WPF's version needed <c>View.CurrentItem</c>/<c>MoveCurrentTo*</c> just to walk
/// backwards through the visible order is replaced here with a plain index walk over
/// <c>Collection</c> - see <see cref="GetPreviousEntry"/>/<see cref="MoveOut"/>.
///
/// The dual-collection sync (a hidden <see cref="AllEntriesCollection"/> holding every entry,
/// visible or not, kept in lockstep with the base class's own visible-only <c>Collection</c>) is a
/// near-verbatim port: it is plain <see cref="ObservableCollection{T}"/> bookkeeping with no WPF API
/// in it, and <c>AddRange</c>/<c>RemoveRange</c>/<c>InsertRange</c> are Kapok.Core extension methods
/// (see <c>ExtendDotNetBase/CollectionExtension.cs</c>/<c>ObservableCollectionExtension.cs</c>) this
/// port already has through its Kapok.Core package reference, not WPF-specific helpers.
///
/// Deliberately dropped: WPF's <c>Expand</c>/<c>Collapse</c> both open with an
/// <c>IEditableCollectionView</c> check that commits any in-progress add/edit before mutating the
/// source collection - that is WPF DataGrid's own inline-add-row/edit-lifecycle concept
/// (<c>CollectionViewSource</c>), which this DataSetView layer has no reference to and no
/// equivalent of; <c>CustomDataGrid</c>'s own edit lifecycle (<c>BeginEdit</c>/<c>CommitEdit</c>/
/// <c>CancelEdit</c>) lives entirely on the control, matching how every other DataSetView in this
/// port stays UI-control-free.
/// </summary>
public class AvaloniaHierarchyDataSetView<TEntry> : AvaloniaDataSetView<TEntry>, IHierarchyDataSetView<TEntry>
    where TEntry : class, IHierarchyEntry<TEntry>, new()
{
    public AvaloniaHierarchyDataSetView(IServiceProvider serviceProvider, IDataDomainScope dataDomainScope, IEntityService<TEntry>? entityService = null)
        : base(serviceProvider, dataDomainScope, entityService)
    {
        AllEntriesCollection = new ObservableCollection<TEntry>();
        AllEntriesCollection.CollectionChanged += AllEntriesCollection_CollectionChanged;
        Collection.CollectionChanged += Collection_CollectionChanged;

        ExpandAction = new UIAction("Expand", () => Expand((TEntry)Current!), CanExpand);
        CollapseAction = new UIAction("Collapse", () => Collapse((TEntry)Current!), CanCollapse);
        ToggleAction = new UIAction("Toggle", () => Toggle((TEntry)Current!), CanToggle);
        MoveInAction = new UIAction("MoveIn", MoveIn, CanMoveIn);
        MoveOutAction = new UIAction("MoveOut", MoveOut, CanMoveOut);
    }

    public override void Dispose()
    {
        base.Dispose();
        AllEntriesCollection.CollectionChanged -= AllEntriesCollection_CollectionChanged;
        Collection.CollectionChanged -= Collection_CollectionChanged;
    }

    private bool _firstLoad = true;

    private void SetNewEntryDefaults(TEntry entry)
    {
        if (entry.Level == 0)
            entry.IsVisible = true;

        entry.HasChildren = AllEntriesCollection.Any(entry.GetChildrenPredicate());

        if (entry.IsExpanded)
            entry.IsExpanded = false;

        if (entry.Parent != null && !entry.Parent.HasChildren)
        {
            entry.Parent.HasChildren = true;
        }
    }

    protected override void OnLoad(List<TEntry> newEntries)
    {
        var entriesToHide = new List<TEntry>();

        foreach (var newEntry in newEntries)
        {
            if (_firstLoad)
            {
                newEntry.IsExpanded = default;
                newEntry.IsVisible = default;
                newEntry.HasChildren = default;
            }

            SetNewEntryDefaults(newEntry);

            if (!newEntry.IsVisible)
                entriesToHide.Add(newEntry);
        }

        lock (_syncCollectionLockObject)
        {
            _syncCollections = true;
            AllEntriesCollection.AddRange(newEntries);
            _syncCollections = false;
        }

        newEntries.RemoveRange(entriesToHide);

        _firstLoad = false;

        base.OnLoad(newEntries);
    }

    #region Collection synchronisation

    private void AllEntriesCollection_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_syncCollections)
            return;

        bool addItems;
        bool oldItems;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                addItems = true;
                oldItems = false;
                break;
            case NotifyCollectionChangedAction.Replace:
                addItems = true;
                oldItems = true;
                break;
            case NotifyCollectionChangedAction.Remove:
                addItems = false;
                oldItems = true;
                break;
            case NotifyCollectionChangedAction.Move:
                lock (_syncCollectionLockObject)
                {
                    _syncCollections = true;
                    Collection.Move(e.OldStartingIndex, e.NewStartingIndex);
                    _syncCollections = false;
                }
                return;
            case NotifyCollectionChangedAction.Reset:
                lock (_syncCollectionLockObject)
                {
                    _syncCollections = true;
                    Collection.Clear();
                    _syncCollections = false;
                }
                return;
            default:
                throw new NotSupportedException();
        }

        if (addItems && e.NewItems != null)
        {
            var newVisibleItems = new List<TEntry>();

            foreach (var newItem in e.NewItems.Cast<TEntry>())
            {
                SetNewEntryDefaults(newItem);

                if (newItem.IsVisible)
                    newVisibleItems.Add(newItem);
            }

            lock (_syncCollectionLockObject)
            {
                _syncCollections = true;
                if (newVisibleItems.Count == 1)
                    Collection.Add(newVisibleItems[0]);
                else if (newVisibleItems.Count > 1)
                    Collection.AddRange(newVisibleItems);
                _syncCollections = false;
            }
        }

        if (oldItems && e.OldItems != null)
        {
            var oldItemsToRemove = e.OldItems.Cast<TEntry>().Where(Collection.Contains).ToList();

            lock (_syncCollectionLockObject)
            {
                _syncCollections = true;
                if (oldItemsToRemove.Count == 1)
                    Collection.Remove(oldItemsToRemove[0]);
                else if (oldItemsToRemove.Count > 1)
                    Collection.RemoveRange(oldItemsToRemove);
                _syncCollections = false;
            }
        }
    }

    private void Collection_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_syncCollections)
            return;

        bool addItems;
        bool oldItems;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                addItems = true;
                oldItems = false;
                break;
            case NotifyCollectionChangedAction.Replace:
                addItems = true;
                oldItems = true;
                break;
            case NotifyCollectionChangedAction.Remove:
                addItems = false;
                oldItems = true;
                break;
            case NotifyCollectionChangedAction.Move:
                lock (_syncCollectionLockObject)
                {
                    _syncCollections = true;
                    AllEntriesCollection.Move(e.OldStartingIndex, e.NewStartingIndex);
                    _syncCollections = false;
                }
                return;
            case NotifyCollectionChangedAction.Reset:
                lock (_syncCollectionLockObject)
                {
                    _syncCollections = true;
                    AllEntriesCollection.Clear();
                    _syncCollections = false;
                }
                return;
            default:
                throw new NotSupportedException();
        }

        if (addItems && e.NewItems != null)
        {
            lock (_syncCollectionLockObject)
            {
                _syncCollections = true;
                if (e.NewStartingIndex != -1)
                {
                    if (e.NewItems.Count == 1)
                        AllEntriesCollection.Insert(e.NewStartingIndex, e.NewItems.Cast<TEntry>().First());
                    else if (e.NewItems.Count > 1)
                        AllEntriesCollection.InsertRange(e.NewStartingIndex, e.NewItems.Cast<TEntry>());
                }
                else
                {
                    if (e.NewItems.Count == 1)
                        AllEntriesCollection.Add(e.NewItems.Cast<TEntry>().First());
                    else if (e.NewItems.Count > 1)
                        AllEntriesCollection.AddRange(e.NewItems.Cast<TEntry>());
                }
                _syncCollections = false;
            }
        }

        if (oldItems && e.OldItems != null)
        {
            // Parents whose only child is being removed - checked *before* the removal (HasChildren
            // needs to flip back to false once nothing satisfies GetChildrenPredicate anymore).
            var parentsToCheck = (
                from item in e.OldItems.Cast<TEntry>()
                group item by item.Parent
                into g
                where g.Key != null &&
                      !e.OldItems.Cast<TEntry>().Contains(g.Key) &&
                      g.Key.HasChildren
                select g.Key
            ).ToList();

            lock (_syncCollectionLockObject)
            {
                _syncCollections = true;
                if (e.OldItems.Count == 1)
                    AllEntriesCollection.Remove(e.OldItems.Cast<TEntry>().First());
                else if (e.OldItems.Count > 1)
                    AllEntriesCollection.RemoveRange(e.OldItems.Cast<TEntry>());
                _syncCollections = false;
            }

            foreach (var parentItem in parentsToCheck)
            {
                parentItem.HasChildren = AllEntriesCollection.Any(parentItem.GetChildrenPredicate());
            }
        }
    }

    private readonly object _syncCollectionLockObject = new();
    private bool _syncCollections;

    /// <summary>A collection containing all entries, including the not visible ones.</summary>
    private ObservableCollection<TEntry> AllEntriesCollection { get; }

    #endregion

    #region Tree management commands

    public IAction ExpandAction { get; }
    public IAction CollapseAction { get; }

    /// <summary>Collapses when expanded, expands when collapsed.</summary>
    public IAction ToggleAction { get; }

    public IAction MoveInAction { get; }
    public IAction MoveOutAction { get; }

    protected virtual void Expand(TEntry entry)
    {
        if (!entry.IsExpanded)
            entry.IsExpanded = true;

        var childEntries = new List<TEntry>();
        foreach (var child in AllEntriesCollection.Where(entry.GetChildrenPredicate()))
        {
            if (!child.IsVisible)
            {
                child.IsVisible = true;
                childEntries.Add(child);
            }
        }

        RestoringCurrentAcross(() =>
        {
            lock (_syncCollectionLockObject)
            {
                _syncCollections = true;
                Collection.InsertRange(Collection.IndexOf(entry) + 1, childEntries);
                _syncCollections = false;
            }
        });
    }

    /// <summary>
    /// Runs <paramref name="mutateCollection"/> (an Insert/Remove against the base class's own
    /// <c>Collection</c>) and restores <see cref="DataSetView{TEntry}.Current"/> to whatever it was
    /// beforehand. Needed because of a real bug shared with Kapok.View.Wpf's HierarchyDataSetView,
    /// confirmed by running this and watching Current change to an entry nobody ever assigned it to
    /// (a plain Collapse of "Home" left Current pointing at "Garden" - the last item in that
    /// removal's own <c>NotifyCollectionChangedEventArgs.OldItems</c>): the shared, framework-
    /// agnostic <c>DataSetView&lt;TEntry&gt;.Collection_CollectionChanged</c> (private, in
    /// Kapok.View - out of scope to change from here) treats *every* removal from <c>Collection</c>
    /// as a real delete, including setting the private <c>_current</c> field directly
    /// (<c>_current = oldEntry;</c>, bypassing the <c>Current</c> property setter and its
    /// <c>OnPropertyChanged</c> call entirely) for whichever entry it last processed - so hiding a
    /// node's children on Collapse, or revealing them on Expand, silently reassigns Current to
    /// whatever was hidden/shown last, with no notification this class's CanExpand/CanCollapse/
    /// CanToggle caches (or any other subscriber) can react to. Restoring Current explicitly here,
    /// through the real property setter, is what makes those caches (and everything else watching
    /// Current) invalidate correctly again.
    ///
    /// The same base-class code path also calls EntityService.Delete/Init on every hide/show,
    /// treating structural visibility changes as real create/delete - confirmed harmless for
    /// TaskCategory specifically (Init only touches [DefaultValue]/[AutoGenerateValue(Identity)]
    /// properties, and TaskCategory has neither), but a genuine risk for any hierarchy entity that
    /// does have either, or for a node collapsed/expanded after being saved rather than while still
    /// new/uncommitted. A real, deeper fix belongs in Kapok.View's own Collection_CollectionChanged -
    /// out of scope for this port, flagged here rather than silently worked around.
    /// </summary>
    private void RestoringCurrentAcross(Action mutateCollection)
    {
        var current = Current;
        mutateCollection();
        Current = current;
    }

    private bool? _canExpandCache;
    protected virtual bool CanExpand()
    {
        if (Current == null) return false;

        return _canExpandCache ??=
            !((TEntry)Current).IsExpanded && AllEntriesCollection.Any(((TEntry)Current).GetChildrenPredicate());
    }

    private void CollapseLoop(TEntry currentEntry, List<TEntry> entriesToCollapse)
    {
        if (currentEntry.IsExpanded)
            currentEntry.IsExpanded = false;

        foreach (var child in AllEntriesCollection.Where(currentEntry.GetChildrenPredicate()))
        {
            child.IsVisible = false;
            entriesToCollapse.Add(child);

            if (child.IsExpanded)
                CollapseLoop(child, entriesToCollapse);
        }
    }

    protected virtual void Collapse(TEntry entry)
    {
        var entriesToCollapse = new List<TEntry>();
        CollapseLoop(entry, entriesToCollapse);

        RestoringCurrentAcross(() =>
        {
            lock (_syncCollectionLockObject)
            {
                _syncCollections = true;
                Collection.RemoveRange(entriesToCollapse);
                _syncCollections = false;
            }
        });
    }

    private bool? _canCollapseCache;
    protected virtual bool CanCollapse()
    {
        if (Current == null) return false;

        return _canCollapseCache ??=
            ((TEntry)Current).IsExpanded && AllEntriesCollection.Any(((TEntry)Current).GetChildrenPredicate());
    }

    protected virtual void Toggle(TEntry entry)
    {
        if (entry.IsExpanded)
            Collapse(entry);
        else
            Expand(entry);
    }

    private bool? _canToggleCache;
    protected virtual bool CanToggle()
    {
        if (Current == null) return false;

        return _canToggleCache ??= AllEntriesCollection.Any(((TEntry)Current).GetChildrenPredicate());
    }

    protected virtual void MoveIn()
    {
        if (Current == null) return;
        var current = (TEntry)Current;
        if (current.Level == 0) return;

        var currentParent = current.Parent;
        current.Parent = null!;
        current.Level -= 1;

        if (currentParent != null)
        {
            currentParent.HasChildren = AllEntriesCollection.Any(currentParent.GetChildrenPredicate());
            if (!currentParent.HasChildren)
                currentParent.IsExpanded = false;
            // Sort-order re-placement (moving the entry to the end/start of its new siblings) is
            // left to the caller for the same reason WPF's own version left it as a TODO -
            // ISortableEntity's own SortUp/SortDown/drag-drop machinery (Phase 7 item 6) already
            // owns re-sequencing SortOrder and does it more completely than a one-off patch here
            // could.
        }
    }

    private bool? _canMoveInCache;
    protected virtual bool CanMoveIn()
    {
        if (_canMoveInCache.HasValue)
            return _canMoveInCache.Value;

        _canMoveInCache = Current != null && ((TEntry)Current).Level > 0;
        return _canMoveInCache.Value;
    }

    /// <summary>
    /// The entry immediately above <see cref="Current"/> in the visible <c>Collection</c> order -
    /// the direct Avalonia equivalent of WPF's <c>View.MoveCurrentToPrevious()</c> read against
    /// <c>View.CurrentItem</c>, but as a plain index lookup: <c>Collection</c> already *is* the
    /// display order (see this class's own header comment), so there is no separate view position
    /// to move.
    /// </summary>
    private TEntry? GetPreviousEntry()
    {
        if (Current == null) return null;

        var index = Collection.IndexOf((TEntry)Current);
        return index > 0 ? Collection[index - 1] : null;
    }

    protected virtual void MoveOut()
    {
        if (Current == null) return;
        var current = (TEntry)Current;

        var index = Collection.IndexOf(current);
        if (index <= 0) return;

        // Walk backwards to the nearest preceding entry at the same level as 'current' - that is
        // its would-be new parent (WPF walked the same distance via repeated
        // View.MoveCurrentToPrevious() calls against View.CurrentItem; here it's a plain index
        // decrement against Collection, which needs no "move back afterwards" step at all, since
        // nothing here ever changed Current/any view position in the first place - only WPF's
        // ICollectionView.CurrentItem-as-cursor design needed that restore).
        var candidateIndex = index - 1;
        var candidate = Collection[candidateIndex];
        while (candidate.Level != current.Level)
        {
            candidateIndex--;
            if (candidateIndex < 0)
                return;
            candidate = Collection[candidateIndex];
        }

        var parent = candidate;
        if (!Equals(parent.Parent, current.Parent))
            return;

        current.Parent = parent;
        current.Level = parent.Level + 1;

        if (!parent.HasChildren)
        {
            parent.HasChildren = true;
            parent.IsExpanded = true;
        }
        else if (!parent.IsExpanded)
        {
            Expand(parent);
        }
    }

    private bool? _canMoveOutCache;
    protected virtual bool CanMoveOut()
    {
        if (_canMoveOutCache.HasValue)
            return _canMoveOutCache.Value;

        if (Current == null) return false;
        var current = (TEntry)Current;

        var previous = GetPreviousEntry();
        if (previous == null)
        {
            _canMoveOutCache = false;
            return false;
        }

        _canMoveOutCache = current.Level <= previous.Level;
        return _canMoveOutCache.Value;
    }

    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (propertyName == nameof(Current))
        {
            _canExpandCache = null;
            _canCollapseCache = null;
            _canToggleCache = null;
            _canMoveInCache = null;
            _canMoveOutCache = null;
        }

        base.OnPropertyChanged(propertyName);
    }

    #endregion

    protected override void OnEntryPropertyChanged(TEntry entry, string? propertyName)
    {
        base.OnEntryPropertyChanged(entry, propertyName);

        if (propertyName != nameof(IHierarchyEntry<TEntry>.IsExpanded))
            return;

        if (!entry.HasChildren) // check via cached value if child entries exist
            return;

        if (entry.IsExpanded)
            Expand(entry);
        else
            Collapse(entry);
    }

    protected override bool CanToggleFilterVisible() =>
        // As with WPF's version: the filter row is not supported in hierarchy views.
        false;
}
