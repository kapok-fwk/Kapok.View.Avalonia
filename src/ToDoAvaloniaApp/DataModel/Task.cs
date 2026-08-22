using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Kapok.Entity;

namespace ToDoAvaloniaApp.DataModel;

public class Task : EditableEntityBase
{
    // Client-generated - see TaskList.Id's comment for why an all-zero Guid primary key breaks
    // row identity in the DataGrid.
    private Guid _id = Guid.NewGuid();
    private string _name = string.Empty;
    private string? _description;
    private DateTime? _dueDate;
    private decimal? _estimatedTime;
    private Guid? _taskListId;
    private TaskPriority _priority = TaskPriority.Normal;

    [Key]
    [Display(Name = nameof(Id))]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id
    {
        get => _id;
        set => SetValidateProperty(ref _id, value);
    }

    // DisplayShortName + DisplayDescription are set here (not just Name) so the generated
    // DataGrid column actually exercises Kapok's header rules: the *short* name is what a column
    // header shows, and the long name + description are what its header tooltip shows.
    [Required(AllowEmptyStrings = false)]
    [Display(Name = "Task name", ShortName = "Task", Description = "Short description of what has to be done.")]
    public string Name
    {
        get => _name;
        set => SetValidateProperty(ref _name, value);
    }

    [Display(Name = "Description", Description = "Free-text notes; wraps over multiple lines in the list.")]
    public string? Description
    {
        get => _description;
        set => SetValidateProperty(ref _description, value);
    }

    [Display(Name = "Due date", ShortName = "Due")]
    [DataType(DataType.Date)]
    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetValidateProperty(ref _dueDate, value);
    }

    [Display(Name = "Estimated time (h)", ShortName = "Est. h")]
    [Precision(2)]
    public decimal? EstimatedTime
    {
        get => _estimatedTime;
        set => SetValidateProperty(ref _estimatedTime, value);
    }

    [Display(Name = "Task list")]
    [ForeignKey(nameof(TaskList))]
    public Guid? TaskListId
    {
        get => _taskListId;
        set => SetValidateProperty(ref _taskListId, value);
    }

    [Display(Name = "Priority", Description = "How urgent this task is.")]
    public TaskPriority Priority
    {
        get => _priority;
        set => SetValidateProperty(ref _priority, value);
    }

    public TaskList? TaskList { get; set; }

    public override string ToString()
    {
        return $"Task {Name}";
    }
}
