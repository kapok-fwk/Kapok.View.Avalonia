using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Kapok.Entity;

namespace ToDoAvaloniaApp.DataModel;

public class Task : EditableEntityBase
{
    private Guid _id;
    private string _name = string.Empty;
    private string? _description;
    private DateTime? _dueDate;
    private decimal? _estimatedTime;
    private Guid? _taskListId;

    [Key]
    [Display(Name = nameof(Id))]
    public Guid Id
    {
        get => _id;
        set => SetValidateProperty(ref _id, value);
    }

    [Required(AllowEmptyStrings = false)]
    [Display(Name = nameof(Name))]
    public string Name
    {
        get => _name;
        set => SetValidateProperty(ref _name, value);
    }

    [Display(Name = nameof(Description))]
    public string? Description
    {
        get => _description;
        set => SetValidateProperty(ref _description, value);
    }

    [Display(Name = nameof(DueDate))]
    [DataType(DataType.Date)]
    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetValidateProperty(ref _dueDate, value);
    }

    [Display(Name = nameof(EstimatedTime))]
    [Precision(2)]
    public decimal? EstimatedTime
    {
        get => _estimatedTime;
        set => SetValidateProperty(ref _estimatedTime, value);
    }

    [Display(Name = nameof(TaskListId))]
    [ForeignKey(nameof(TaskList))]
    public Guid? TaskListId
    {
        get => _taskListId;
        set => SetValidateProperty(ref _taskListId, value);
    }

    public TaskList? TaskList { get; set; }

    public override string ToString()
    {
        return $"Task {Name}";
    }
}
