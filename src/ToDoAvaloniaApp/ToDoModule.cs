using Kapok.Data;
using Kapok.Module;
using Kapok.View;
using Kapok.View.Avalonia;
using Kapok.View.Avalonia.Windows;
using ToDoAvaloniaApp.BusinessLogic;
using ToDoAvaloniaApp.DataModel;
using ToDoAvaloniaApp.View;
using Task = ToDoAvaloniaApp.DataModel.Task;

namespace ToDoAvaloniaApp;

public class ToDoModule : ModuleBase
{
    public ToDoModule() : base(nameof(ToDoModule))
    {
        // data model registration
        DataDomain.RegisterEntity<Task, TaskService>();
        DataDomain.RegisterEntity<TaskList>();

        // register default pages for data models
        ViewDomain.RegisterEntityDefaultPage<Task>(typeof(Tasks));
        ViewDomain.RegisterEntityDefaultPage<TaskList>(typeof(TaskLists));

        // MainPage is a plain InteractivePage (see View/MainPage.cs) - it doesn't match any of
        // AvaloniaViewDomain.ConstructWindow's IListPage/IDialogPage/ICardPage fallbacks, so it
        // needs an explicit window constructor, same as WpfViewDomain.RegisterPageWpfWindowConstructor
        // did for the WPF version's MainPageWindow. Reuses the generic PageWindow rather than a
        // dedicated MainPageWindow subclass - no custom chrome needed yet in this phase.
        AvaloniaViewDomain.RegisterPageWindowConstructor<MainPage>(() => new PageWindow());
    }
}
