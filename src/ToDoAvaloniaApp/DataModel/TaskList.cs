using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Kapok.Entity;

namespace ToDoAvaloniaApp.DataModel;

[Display(Name = $"{nameof(TaskList)}_EntityName")]
public class TaskList : EditableEntityBase
{
    private Guid _id;
    private string _name = string.Empty;
    private bool _isArchived;

    [Key]
    [Display(Name = nameof(Id))]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
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
