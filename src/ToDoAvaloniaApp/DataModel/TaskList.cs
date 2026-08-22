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

    public override string ToString()
    {
        return $"Task list {Name}";
    }
}
