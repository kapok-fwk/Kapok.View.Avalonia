using Kapok.BusinessLayer;
using Kapok.Data;
using Task = ToDoAvaloniaApp.DataModel.Task;

namespace ToDoAvaloniaApp.BusinessLogic;

public class TaskService : EntityDeferredCommitService<Task>
{
    public TaskService(IDataDomainScope dataDomainScope, IRepository<Task> repository) : base(dataDomainScope, repository)
    {
    }

    public override void Init(Task entry)
    {
        base.Init(entry);
        entry.Id = Guid.NewGuid();
    }
}
