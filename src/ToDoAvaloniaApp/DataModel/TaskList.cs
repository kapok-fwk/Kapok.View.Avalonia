using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Kapok.Entity;

namespace ToDoAvaloniaApp.DataModel;

[Display(Name = $"{nameof(TaskList)}_EntityName")]
public class TaskList : EditableEntityBase
{
    // Client-generated, and deliberately NOT [DatabaseGenerated(Identity)]: Kapok's EntityBase
    // implements Equals/GetHashCode over the *primary key*, so entities created but not yet
    // written to the database all compared equal while their Guid stayed Guid.Empty. Avalonia's
    // DataGrid resolves an item to its row through the collection's IndexOf, which then returned
    // 0 for every row - three seeded rows rendered as "List 1 / List 2 / List 1" (confirmed by
    // dumping each DataGridRow's own DataContext, not guessed). Sqlite cannot generate a Guid
    // key server-side either, so nothing was ever going to fill it in. ToDoWpfApp's copy of this
    // entity has the same latent problem; it just never showed there because that sample was
    // never exercised with several unsaved rows at once.
    private Guid _id = Guid.NewGuid();
    private string _name = string.Empty;
    private bool _isArchived;
    private byte[]? _icon;

    [Key]
    [Display(Name = nameof(Id))]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id
    {
        get => _id;
        set => SetValidateProperty(ref _id, value);
    }

    [Display(Name = nameof(Name))]
    [LookupColumn]
    [Required(AllowEmptyStrings = false)]
    public string Name
    {
        get => _name;
        set => SetValidateProperty(ref _name, value);
    }

    [Display(Name = nameof(IsArchived))]
    public bool IsArchived
    {
        get => _isArchived;
        set => SetValidateProperty(ref _isArchived, value);
    }

    /// <summary>
    /// Phase 8 item 5 verification target for DataGridImageColumn/LookupComboBox's
    /// [BinaryImage] branch - a small avatar/attachment image, the same shape a real Kapok entity
    /// would use it for. Seeded from a real PNG asset (see App.cs), not a hand-rolled byte blob.
    /// </summary>
    [Display(Name = "Icon")]
    [BinaryImage]
    public byte[]? Icon
    {
        get => _icon;
        set => SetValidateProperty(ref _icon, value);
    }

    /// <summary>
    /// Phase 8 item 5 verification target for DataGridInfoImageColumn's [InfoImages] branch - a
    /// row of small status badges. Seeded with real Kapok.View.ImageManager icon names (see
    /// App.cs), matching what ImageNameToImageSourceConverter (the per-item image resolver this
    /// column uses) actually accepts.
    /// </summary>
    [Display(Name = "Badges")]
    [InfoImages]
    public ObservableCollection<string> Badges { get; } = new();

    public override string ToString()
    {
        return $"Task list {Name}";
    }
}
