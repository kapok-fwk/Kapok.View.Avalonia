using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Kapok.Entity;

namespace ToDoAvaloniaApp.DataModel;

/// <summary>
/// A category a task can belong to, organised as a tree (a category can have a parent category).
///
/// Added for Phase 7 item 4: <see cref="ColumnPropertyView.ShowHierarchicalTree"/> makes
/// CustomDataGrid generate a DataGridTreeTextColumn, which binds to the
/// <see cref="IHierarchyEntry{TEntry}"/> members Level/HasChildren/IsExpanded. Nothing else in
/// ToDoAvaloniaApp is hierarchical, so without this entity that column could only be verified by
/// reading the code.
/// </summary>
[Display(Name = "Task category")]
public class TaskCategory : EditableEntityBase, IHierarchyEntry<TaskCategory>, ISortableEntity
{
    // Client-generated - see TaskList.Id's comment on why an all-zero Guid primary key breaks row
    // identity in the DataGrid.
    private Guid _id = Guid.NewGuid();
    private string _name = string.Empty;
    private Guid? _parentId;
    private int _level;
    private bool _isExpanded = true;
    private bool _isVisible = true;
    private bool _hasChildren;
    private int _sortOrder;

    [Key]
    [Display(Name = nameof(Id))]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id
    {
        get => _id;
        set => SetValidateProperty(ref _id, value);
    }

    [Required(AllowEmptyStrings = false)]
    [Display(Name = "Category", Description = "Name of the category; nested categories are shown indented.")]
    [LookupColumn]
    public string Name
    {
        get => _name;
        set => SetValidateProperty(ref _name, value);
    }

    [Display(Name = "Parent category")]
    [ForeignKey(nameof(Parent))]
    public Guid? ParentId
    {
        get => _parentId;
        set => SetValidateProperty(ref _parentId, value);
    }

    /// <summary>
    /// Explicit display order. Implementing <see cref="ISortableEntity"/> is what makes this entity
    /// reorderable by dragging rows (Phase 7 item 6) - the grid offers the drag only for entities
    /// that have somewhere to persist the new order, and writes SortOrder back after a drop, the
    /// same field SortableDataSetView.SortUp/SortDown maintain.
    /// </summary>
    [Display(Name = "Order")]
    public int SortOrder
    {
        get => _sortOrder;
        set => SetValidateProperty(ref _sortOrder, value);
    }

    #region IHierarchyEntry<TaskCategory>

    private TaskCategory? _parent;

    /// <summary>
    /// Phase 8 item 4: keeps <see cref="ParentId"/> (the real, mapped FK - what
    /// <see cref="GetChildrenPredicate"/> and every query actually use) in sync whenever the
    /// navigation reference changes, since nothing else in this sample entity does (no lazy-loading
    /// proxy, no EF navigation-fixup) - AvaloniaHierarchyDataSetView's MoveIn/MoveOut assign this
    /// property directly, the same way WPF's HierarchyDataSetView does, and without this the FK
    /// would silently stop matching the in-memory tree the moment a node moved.
    /// </summary>
    public TaskCategory Parent
    {
        get => _parent!;
        set
        {
            _parent = value;
            ParentId = value?.Id;
        }
    }

    [Display(Name = "Level")]
    public int Level
    {
        get => _level;
        set => SetValidateProperty(ref _level, value);
    }

    [NotMapped]
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetValidateProperty(ref _isExpanded, value);
    }

    [NotMapped]
    public bool IsVisible
    {
        get => _isVisible;
        set => SetValidateProperty(ref _isVisible, value);
    }

    [NotMapped]
    public bool HasChildren
    {
        get => _hasChildren;
        set => SetValidateProperty(ref _hasChildren, value);
    }

    public Func<TaskCategory, bool> GetChildrenPredicate() => category => category.ParentId == Id;

    #endregion

    public override string ToString() => $"Task category {Name}";
}
