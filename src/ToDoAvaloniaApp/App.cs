using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kapok.BusinessLayer;
using Kapok.Data;
using Kapok.Data.EntityFrameworkCore;
using Kapok.Module;
using Kapok.View;
using Kapok.View.Avalonia;
using Kapok.View.Avalonia.Controls;
using Kapok.View.Avalonia.Dock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ToDoAvaloniaApp.View;

namespace ToDoAvaloniaApp;

public class App : Application
{
    public IHost Host { get; private set; } = null!;

    public T GetService<T>() where T : class
    {
        if (Host.Services.GetService(typeof(T)) is not T service)
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices.");

        return service;
    }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ModuleEngine.InitiateModule(typeof(ToDoModule));

        DataDomain.DefaultEntityServiceType = typeof(EntityDeferredCommitService<>);

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .UseDefaultServiceProvider((_, options) => { options.ValidateOnBuild = true; })
            .ConfigureServices((_, services) =>
            {
                // View logic - AvaloniaDockViewDomain (Phase 3) gives card/list pages a
                // Dock.Avalonia-hosted window instead of AvaloniaViewDomain's plain ContentControl
                // one; it's a strict superset (same Ribbon/KeyBindings wiring, see DockPageWindow),
                // so this is the only ViewDomain ToDoAvaloniaApp needs going forward.
                services.AddSingleton<IViewDomain, AvaloniaDockViewDomain>(serviceProvider =>
                    new AvaloniaDockViewDomain(ShutdownApplication, serviceProvider));

                // Data logic
                services.AddSingleton<IDataDomain>(serviceProvider =>
                {
                    var optionsBuilder = new DbContextOptionsBuilder();
                    optionsBuilder.UseInMemoryDatabase("ToDos");

                    return new EFCoreDataDomain(optionsBuilder.Options)
                    {
                        ServiceProvider = serviceProvider
                    };
                });
                services.TryAdd(ServiceDescriptor.Scoped<IDataDomainScope>(p =>
                    new EFCoreDataDomainScope(p.GetRequiredService<IDataDomain>(), p)));
                services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(EFCoreRepository<>)));

                // Views and ViewModels
                services.AddTransient<MainPage>();
                services.AddTransient<TaskLists>();
                services.AddTransient<Tasks>();
                services.AddTransient<TestPage>();
            }).Build();

        // Verification-only hook (see Program.cs's KAPOK_HEADLESS_SCREENSHOT): lets a headless
        // screenshot target a page other than MainPage, e.g. "TaskLists" - MainPage's own Base
        // menu is currently empty by design (its actions target a separate "Main" menu meant for
        // a navigation-bar surface that doesn't exist until Phase 3's DocumentPageCollectionPage
        // lands, see MainPage.cs), so it alone can't prove the Ribbon renders real buttons.
        // "TaskCard" (Phase 5) shows Tasks like "Tasks" does - TaskCard itself isn't a
        // directly-showable top-level page (CardPage<TEntry> requires an IDataSetView<TEntry>
        // constructor argument DI can't resolve on its own), it's opened automatically by
        // Tasks.OpenCardPageAction when the seed logic below creates a new Task, same real path
        // a user would take.
        // Tracks the last window opened (Tasks, then TaskCard once seeded below) - matches
        // Program.cs's own WindowOpenedEvent class-handler technique and its comment on why
        // ApplicationLifetime.Windows isn't safe to read at this point in startup.
        Window? lastOpenedWindow = null;
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => lastOpenedWindow = w);

        var pageTypeName = Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_PAGE");
        IPage page = pageTypeName switch
        {
            "TaskLists" => GetService<TaskLists>(),
            "Tasks" => GetService<Tasks>(),
            "TaskCard" => GetService<Tasks>(),
            "TestPage" => GetService<TestPage>(),
            _ => GetService<MainPage>()
        };
        page.Show();

        // Phase 4 verification-only: ToDoAvaloniaApp seeds no data, so the DataGrid added to
        // ListPageView would otherwise render zero rows either way (correctly, but
        // unconvincingly). Creating one real entry through the normal CreateNewEntryAction
        // pipeline - not injecting a fake row directly - proves ItemsSource actually renders
        // real, live DataSet.Collection content. In-memory only, gated behind an env var, so it
        // never runs during a normal `dotnet run`.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_SEED") == "1" &&
            page is IDataPage { DataSet: { } dataSet })
        {
            // Phase 5 verification: TaskCard's LookupComboBox needs a real TaskList to show in
            // its dropdown grid. Seeded through TaskLists' own real DataSet/business-layer
            // pipeline (CreateNewEntryAction, then Save so it's visible to the *separate*
            // IDataDomainScope TaskCard's LookupDefinition queries through - see
            // AvaloniaPropertyLookupView.Refresh), not injected directly.
            if (pageTypeName == "TaskCard")
            {
                var taskListsDataSet = GetService<TaskLists>().DataSet!;
                taskListsDataSet.CreateNewEntryAction.Execute();
                taskListsDataSet.Current!.Name = "Groceries";
                taskListsDataSet.Save();

                // The page-level CreateNewEntryAction (ListPage<TEntry>.CreateNewEntry(), not
                // the DataSet's own one used below for the other pages) is what actually checks
                // and triggers OpenCardPageAction - calling dataSet.CreateNewEntryAction directly
                // would create the Task but skip opening TaskCard entirely.
                ((IDataPage)page).CreateNewEntryAction.Execute();
            }
            else
            {
                dataSet.CreateNewEntryAction.Execute();
            }
        }

        // Phase 5 verification: proves LookupComboBox's dropdown DataGrid actually renders real
        // lookup rows, not just that the (closed) combo box exists. Opens the dropdown on
        // whichever LookupComboBox is in the just-shown window's visual tree, after the seed
        // block above has already created and opened the TaskCard dialog. At this point in
        // startup nothing has laid out yet (Window.Show() doesn't synchronously build the page's
        // control tree), so TaskCardView's AttachedToVisualTree handler - which is what actually
        // assigns LookupComboBox.ItemsSource - hasn't run yet either. RunJobs() here forces that
        // layout/template pass to happen now instead of waiting for Program.cs's own capture
        // loop, so the control can actually be found and opened before that loop takes over.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_OPEN_LOOKUP") == "1")
        {
            Dispatcher.UIThread.RunJobs();

            // Confirmed the hard way (a real headless run, not a guess): opening a Popup inside
            // a RibbonWindow throws "Unable to create IPopupImpl and no overlay layer is found
            // for the target control". AvaloniaControls.Ribbon.Desktop.Flowery's own Window
            // template has no VisualLayerManager with popup-overlay routing enabled the way a
            // stock Avalonia Window's built-in template does - and PageWindow.cs's own
            // VisualLayerManager wrapper (added for exactly this) can only fix general overlays
            // (EnableOverlayLayer is public); VisualLayerManager.EnablePopupOverlayLayer is
            // internal to Avalonia.Controls, unreachable from Kapok.View.Avalonia. On a real
            // desktop backend this never triggers at all - real platforms always support a true
            // native popup window, so Popup never needs the overlay-layer path there; this is a
            // headless-testing-only gap. Flipping the internal flag via reflection here (not in
            // production code) is the only way to actually see the popup's real rendered pixels
            // in a headless screenshot rather than just trusting the control never crashes.
            var visualLayerManager = lastOpenedWindow?.GetVisualDescendants()
                .OfType<VisualLayerManager>().FirstOrDefault();
            typeof(VisualLayerManager)
                .GetProperty("EnablePopupOverlayLayer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(visualLayerManager, true);

            var lookupComboBox = lastOpenedWindow?.GetVisualDescendants()
                .OfType<LookupComboBox>().FirstOrDefault();
            if (lookupComboBox != null)
                lookupComboBox.IsDropDownOpen = true;

            // Phase 5 verification: proves PopupThumbResizeBehavior actually resizes the
            // dropdown, not just that it attaches without throwing. Raises the corner resize
            // Thumb's real DragDeltaEvent directly - a real mouse-drag simulated via
            // Avalonia.Headless's MouseDown/MouseMove/MouseUp was tried first and confirmed
            // unreliable here: the click routed to the DataGrid underneath (a 10x10 target in a
            // 120x66 popup) and closed the dropdown instead of hitting the Thumb, a
            // headless-hit-testing-precision problem, not a PopupThumbResizeBehavior bug -
            // Thumb.DragDelta is the actual event the behavior listens to either way, so raising
            // it directly still verifies the same code a real drag would drive, just without
            // depending on headless pointer-hit-testing landing on a tiny target.
            if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_RESIZE_LOOKUP") == "1")
            {
                Dispatcher.UIThread.RunJobs();

                var grid = lastOpenedWindow!.GetVisualDescendants().OfType<DataGrid>()
                    .FirstOrDefault()?.GetVisualParent() as Grid;
                var border = grid?.GetVisualParent() as Border;
                var thumb = grid?.GetVisualChildren().OfType<Thumb>()
                    .FirstOrDefault(t => t.Cursor?.ToString()?.Contains("SizeAll") == true);

                if (border != null && thumb != null)
                {
                    var sizeBefore = (double.IsNaN(border.Width) ? border.Bounds.Width : border.Width,
                        double.IsNaN(border.Height) ? border.Bounds.Height : border.Height);
                    thumb.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent, Vector = new Vector(60, 40) });
                    Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_RESIZE_LOOKUP: popup size before={sizeBefore} after=({border.Width},{border.Height})");
                }
                else
                {
                    Console.WriteLine("KAPOK_HEADLESS_SCREENSHOT_RESIZE_LOOKUP: corner thumb or popup border not found");
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShutdownApplication(int exitCode)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown(exitCode);
    }
}
