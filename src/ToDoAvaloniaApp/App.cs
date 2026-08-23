using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
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
using Kapok.View.Avalonia.Report;
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

                // Data logic - Sqlite over a single open in-memory connection (not the InMemory
                // provider used through Phase 4) - see ToDoAvaloniaApp.csproj's comment on why
                // Phase 5's Report entities forced this switch. The connection has to be opened
                // and kept alive for the app's lifetime: Sqlite's ":memory:" database is deleted
                // the moment its one connection closes, so this can't just be an options string
                // handed to UseSqlite() the way a real file path could be.
                var sqliteConnection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
                sqliteConnection.Open();

                services.AddSingleton<IDataDomain>(serviceProvider =>
                {
                    var optionsBuilder = new DbContextOptionsBuilder();
                    optionsBuilder.UseSqlite(sqliteConnection);

                    var dataDomain = new EFCoreDataDomain(optionsBuilder.Options)
                    {
                        ServiceProvider = serviceProvider
                    };

                    // Sqlite is a real relational engine (unlike the InMemory provider), so its
                    // schema has to actually be created - EnsureCreated() builds it straight from
                    // the current model rather than requiring real EF migrations, appropriate for
                    // a throwaway in-memory sample database.
                    using var dbContext = dataDomain.ConstructNewDbContext();
                    dbContext.Database.EnsureCreated();

                    return dataDomain;
                });
                services.TryAdd(ServiceDescriptor.Scoped<IDataDomainScope>(p =>
                    new EFCoreDataDomainScope(p.GetRequiredService<IDataDomain>(), p)));
                services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(EFCoreRepository<>)));

                // Views and ViewModels
                services.AddTransient<MainPage>();
                services.AddTransient<TaskLists>();
                services.AddTransient<Tasks>();
                services.AddTransient<TaskCategories>();
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

        // Phase 5 verification: proves PageContentTemplate's new OnLoadingAction/OnLoadedAction
        // wiring actually makes ListPage<TEntry>.OnLoaded()'s DataSet.Load() run - i.e. that a
        // freshly-shown list page fetches pre-existing rows from the repository, not just rows
        // created live in its own session. Seeds a TaskList through a throwaway TaskLists
        // instance (created, saved, never shown - "Show" is deliberately never called on it), then
        // shows a completely separate, fresh TaskLists instance below; the row can only appear
        // there via a real Load(), since it never touched that instance's own Collection.
        if (pageTypeName == "TaskListsReload")
        {
            var seedTaskLists = GetService<TaskLists>();
            seedTaskLists.DataSet!.CreateNewEntryAction.Execute();
            seedTaskLists.DataSet.Current!.Name = "Reloaded list";
            seedTaskLists.DataSet.Save();
        }

        IPage page = pageTypeName switch
        {
            "TaskLists" => GetService<TaskLists>(),
            "TaskListsReload" => GetService<TaskLists>(),
            "Tasks" => GetService<Tasks>(),
            "TaskCategories" => GetService<TaskCategories>(),
            "TaskCard" => GetService<Tasks>(),
            "TestPage" => GetService<TestPage>(),
            _ => GetService<MainPage>()
        };
        page.Show();

        // Now that PageContentTemplate actually wires OnLoadingAction/OnLoadedAction (this
        // phase's PageControlStyling fix), the page's view needs a real layout pass to exist
        // before the seed logic below touches its DataSet - otherwise a seeded
        // CreateNewEntryAction can run *before* ListPage<TEntry>.OnLoaded()'s DataSet.Load(),
        // which deadlocks for Task specifically (confirmed the hard way: hung indefinitely,
        // isolated by bisecting instrumented Console output - a real edge case in the shared
        // EntityDeferredCommitService query-rewriting layer when Load() runs against a DataSet
        // that already holds an uncommitted entity with a FK navigation property, not something
        // this port's own code can fix). This ordering - page shown and fully loaded before any
        // script touches it - also just matches what a real user's click-driven flow would
        // always look like anyway; a seed script skipping straight from Show() to
        // CreateNewEntryAction was the artificial part.
        Dispatcher.UIThread.RunJobs();

        // Phase 8 item 7 verification: TestPage now opens (was NotSupportedException since Phase
        // 1 - see ToDoModule.cs) and, being Dock-hosted with a real DetailPages entry, proves
        // InteractivePage.DetailPages -> DockPageWindow's ToolDock wiring for the first time
        // anywhere in this port (see DockPageWindow.cs's own "not live-verified" comment).
        if (pageTypeName == "TestPage" && page is TestPage testPage)
        {
            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }

            var toolTitleFound = lastOpenedWindow?.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text == "Task lists (detail)") ?? false;
            Console.WriteLine($"KAPOK_TEST_PAGE: detailPagesCount={testPage.DetailPages.Count} " +
                              $"detailPageTitle={testPage.DetailPages.FirstOrDefault()?.Title} " +
                              $"toolPaneTitleRendered={toolTitleFound}");
        }

        // Real multi-document docking verification: opens two pages as real documents inside
        // MainPage's own DocumentPageCollectionWindow (via ShowDocumentPage, not Show() - a
        // separate top-level window) - proving DocumentPageCollectionWindow's DocumentPages sync
        // (both tabs actually render) and its two-way active-document tracking
        // (CurrentDocumentPage -> ActiveDockable when each is shown, then the same property read
        // back after simulating a tab switch by setting it directly - headless mode has no real
        // pointer to click a tab with, but this exercises the same SyncActiveDockableFromCurrentDocumentPage
        // path a click would end up driving through Factory.SetActiveDockable).
        if (pageTypeName == "MainPageDocking" && page is MainPage mainPage)
        {
            var taskLists = GetService<TaskLists>();
            var docTasksPage = GetService<Tasks>();

            mainPage.ShowDocumentPage(taskLists);
            mainPage.ShowDocumentPage(docTasksPage);

            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }

            var tabTitlesRendered = lastOpenedWindow?.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Text == taskLists.Title || t.Text == docTasksPage.Title)
                .Select(t => t.Text)
                .ToList() ?? new List<string?>();

            Console.WriteLine($"KAPOK_MAIN_PAGE_DOCKING: documentPagesCount={mainPage.DocumentPages.Count} " +
                              $"currentDocumentPage={mainPage.CurrentDocumentPage?.Title} " +
                              $"tabsRendered=[{string.Join(", ", tabTitlesRendered)}]");

            mainPage.CurrentDocumentPage = taskLists;
            Console.WriteLine($"KAPOK_MAIN_PAGE_DOCKING: afterSwitchBack currentDocumentPage={mainPage.CurrentDocumentPage?.Title}");

            // Closing a document must go through IPage.CloseAction (HostFactory.CloseDockable's
            // whole reason to exist - see its own comment) so DocumentPages actually shrinks, not
            // just the dock's own tab list.
            docTasksPage.CloseAction.Execute();
            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            var tasksTabStillRendered = lastOpenedWindow?.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text == docTasksPage.Title) ?? false;
            Console.WriteLine($"KAPOK_MAIN_PAGE_DOCKING: afterClose documentPagesCount={mainPage.DocumentPages.Count} " +
                              $"tasksTabStillRendered={tasksTabStillRendered}");
        }

        // Phase 7 item 1 verification: switching the page's current list view is a real Kapok
        // feature (WPF's ListPageControl toolbar has a menu for it) and the one thing that
        // actually re-drives CustomDataGrid's ColumnsSource CollectionChanged path at runtime -
        // ListPage<TEntry>.OnCurrentListViewChanged clears DataSet.Columns and re-adds the new
        // view's columns. Setting it to "none" clears the columns entirely, which is what
        // exercises the plain-reflection AutoGeneratingColumn fallback (and the metadata that
        // handler applies to reflection-generated columns).
        // Runs *before* the seed block below, not after: OnCurrentListViewChanged calls
        // RequestSaveData + DataSet.Refresh, which hits the same EntityDeferredCommitService
        // deadlock Phase 5 already documented (a Load/Refresh against a DataSet holding an
        // uncommitted entity with an FK navigation property) - confirmed the hard way here again
        // by hanging indefinitely when it ran after the seed. A real user switches list views on a
        // saved list, so this ordering is also the realistic one.
        var listViewName = Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_LIST_VIEW");
        if (listViewName != null)
        {
            var currentListViewProperty = page.GetType().GetProperty("CurrentListView")!;
            var listViewsProperty = page.GetType().GetProperty("ListViews")!;
            var listViews = ((IEnumerable<DataSetListView>)listViewsProperty.GetValue(page)!).ToList();
            var selected = listViewName == "none"
                ? null
                : listViews.FirstOrDefault(v => v.Name == listViewName);
            currentListViewProperty.SetValue(page, selected);
            Console.WriteLine($"KAPOK_LIST_VIEW: available=[{string.Join(", ", listViews.Select(v => v.Name))}] selected={selected?.Name ?? "<null>"}");
            Dispatcher.UIThread.RunJobs();
        }

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
            else if (page is TaskCategories)
            {
                // Phase 7 item 4: a real three-level category tree, so the hierarchy tree column
                // has genuine Level/HasChildren/IsExpanded values to render (indentation,
                // connector lines, expanders on the two parents only).
                var categories = new (string Name, int Level, bool HasChildren)[]
                {
                    ("Home", 0, true),
                    ("Kitchen", 1, true),
                    ("Groceries", 2, false),
                    ("Garden", 1, false),
                    ("Work", 0, false)
                };

                var seededCategoryCount = 0;
                DataModel.TaskCategory? parentLevel0 = null;
                DataModel.TaskCategory? parentLevel1 = null;
                foreach (var (categoryName, level, hasChildren) in categories)
                {
                    dataSet.CreateNewEntryAction.Execute();
                    var category = (DataModel.TaskCategory)dataSet.Current!;
                    category.Name = categoryName;
                    category.Level = level;
                    category.HasChildren = hasChildren;
                    category.Parent = (level switch { 1 => parentLevel0, 2 => parentLevel1, _ => null })!;
                    category.SortOrder = ++seededCategoryCount;
                    if (level == 0) parentLevel0 = category;
                    if (level == 1) parentLevel1 = category;
                }
            }
            else
            {
                dataSet.CreateNewEntryAction.Execute();

                // Phase 7 item 1: a blank row proves the columns exist but not that they render
                // real values in the right format/alignment (a right-aligned decimal, a date
                // formatted through DataType(Date)'s "d", a localized enum caption, wrapped
                // text). Filled through the DataSet's own live Current entity - the real
                // business-layer object, not an injected fake row.
                // A seeded TaskList with an empty Name renders as a visually empty row now that
                // the grid only shows the metadata-defined Name column (before Phase 7 the
                // auto-generated Id column made the row obvious) - naming it keeps the screenshot
                // evidence readable.
                if (page is TaskLists && dataSet.Current is DataModel.TaskList newTaskList)
                {
                    newTaskList.Name = "Groceries";

                    // Phase 8 item 5 verification: real image bytes (a genuine PNG asset already
                    // embedded in this module, not a hand-rolled byte blob) for the [BinaryImage]
                    // column, and real Kapok.View.ImageManager icon names for the [InfoImages]
                    // column - both actually resolve through their respective converters.
                    var iconUri = new Uri("avares://Kapok.View.Avalonia/Resources/Icons/account-book_small.png");
                    using (var iconStream = AssetLoader.Open(iconUri))
                    using (var iconBytes = new MemoryStream())
                    {
                        iconStream.CopyTo(iconBytes);
                        newTaskList.Icon = iconBytes.ToArray();
                    }
                    newTaskList.Badges.Add("bank");
                    newTaskList.Badges.Add("buildings");
                }

                if (page is Tasks && dataSet.Current is DataModel.Task newTask)
                {
                    // A real TaskList to reference, so the lookup column (Phase 7 item 4) has
                    // something to resolve - saved through TaskLists' own DataSet, since the
                    // lookup queries through a separate data-domain scope (same rule Phase 5
                    // found for TaskCard's LookupComboBox).
                    var taskListsDataSet = GetService<TaskLists>().DataSet!;
                    taskListsDataSet.CreateNewEntryAction.Execute();
                    taskListsDataSet.Current!.Name = "Groceries";
                    taskListsDataSet.Save();

                    newTask.TaskListId = taskListsDataSet.Current.Id;
                    newTask.Name = "Buy milk";
                    newTask.Description = "Two litres of whole milk, plus oat milk if the shop has any left.";
                    newTask.EstimatedTime = 1.25m;
                    newTask.DueDate = new DateTime(2026, 3, 17);
                    newTask.Priority = DataModel.TaskPriority.High;
                }
            }
        }

        // Phase 7 item 1 verification: a screenshot alone cannot prove *why* a column looks the
        // way it does (which column type was generated, whether IsHidden really suppressed one,
        // what the header tooltip resolved to, whether IsFilterable was carried over). Prints the
        // real generated column objects off the live grid so the metadata-driven generation can be
        // checked as data, not just visually.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_DUMP_COLUMNS") == "1")
        {
            Dispatcher.UIThread.RunJobs();

            var listGrid = lastOpenedWindow?.GetVisualDescendants().OfType<CustomDataGrid>().FirstOrDefault();
            Console.WriteLine($"KAPOK_DUMP_COLUMNS: autoGenerate={listGrid?.AutoGenerateColumns} " +
                              $"columnsSourceCount={listGrid?.ColumnsSource?.Count} columns={listGrid?.Columns.Count} " +
                              $"items={listGrid?.ItemsSource?.Cast<object>().Count()} " +
                              $"realizedRows={listGrid?.GetVisualDescendants().OfType<DataGridRow>().Count()}");
            Console.WriteLine("KAPOK_DUMP_COLUMNS: renderedCells=[" +
                              string.Join(" | ", (listGrid?.GetVisualDescendants().OfType<DataGridRow>() ?? [])
                                  .Select(r => string.Join(" / ", r.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)))) + "]");
            foreach (var column in listGrid?.Columns ?? new System.Collections.ObjectModel.ObservableCollection<DataGridColumn>())
            {
                var columnViewModel = DataGridColumnExtensions.GetColumnViewModel(column) as ColumnPropertyView;
                var tooltip = DataGridColumnExtensions.GetHeaderTooltip(column) as Panel;
                var tooltipText = tooltip == null
                    ? "<none>"
                    : string.Join(" | ", tooltip.Children.OfType<TextBlock>().Select(t => t.Text));
                Console.WriteLine($"KAPOK_DUMP_COLUMNS:   type={column.GetType().Name} header=\"{column.Header}\" " +
                                  $"property={columnViewModel?.Name} readOnly={column.IsReadOnly} " +
                                  $"width={column.Width.UnitType}:{column.Width.Value} " +
                                  $"classes=[{string.Join(",", column.CellStyleClasses)}] " +
                                  $"canUserFilter={DataGridColumnExtensions.GetCanUserFilter(column)} tooltip=\"{tooltipText}\"");
            }
        }

        // Phase 7 item 2 verification: DataSet.SelectedEntries two-way sync. A screenshot can show
        // highlighted rows but cannot show what the *data* side ended up holding, which is the
        // whole point of this feature - IDataSetSelectionAction<TEntry> consumers
        // (DeleteEntryAction, EditEntryAction, the Ribbon's table-data buttons) cast
        // DataSet.SelectedEntries to IList<TEntry> and act on it. So this exercises both
        // directions on real, saved rows and prints the resulting state, including the concrete
        // list type and whether the real DeleteEntryAction accepts it.
        var selectionMode = Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_SELECTION");
        if (selectionMode != null &&
            page is IDataPage { DataSet: { } selectionDataSet })
        {
            // Three real rows through the normal business-layer pipeline - multi-selection cannot
            // be proven with the single row KAPOK_HEADLESS_SCREENSHOT_SEED creates.
            for (var i = 1; i <= 3; i++)
            {
                selectionDataSet.CreateNewEntryAction.Execute();
                if (selectionDataSet.Current is DataModel.TaskList seededList)
                    seededList.Name = $"List {i}";
            }
            selectionDataSet.Save();
            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }

            var grid = lastOpenedWindow!.GetVisualDescendants().OfType<CustomDataGrid>().First();
            var rows = grid.ItemsSource!.Cast<object>().ToList();

            void DumpRenderedCells(string label) =>
                Console.WriteLine($"KAPOK_SELECTION: {label} renderedCells=[" +
                                  string.Join(" | ", grid.GetVisualDescendants().OfType<DataGridRow>()
                                      .Select(r => $"selected={r.GetValue(DataGridRow.IsSelectedProperty)}/dataContext={(r.DataContext as DataModel.TaskList)?.Name}/y={r.Bounds.Y}/text=" +
                                                   string.Join(",", r.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)))) + "]");

            DumpRenderedCells("after seed");
            if (selectionMode == "seedonly")
            {
                Console.WriteLine($"KAPOK_SELECTION: seedonly items=[{string.Join(", ", rows.Cast<DataModel.TaskList>().Select(t => t.Name))}]");
                goto selectionDone;
            }

            string Describe(System.Collections.IList? list) => list == null
                ? "<null>"
                : $"{list.GetType().Name}<{(list.GetType().IsGenericType ? list.GetType().GetGenericArguments()[0].Name : "?")}>" +
                  $"[{string.Join(", ", list.Cast<object>().Select(o => (o as DataModel.TaskList)?.Name ?? o.ToString()))}]";

            // Direction 1: grid selection -> DataSet.SelectedEntries.
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(rows[0]);
            grid.SelectedItems.Add(rows[2]);
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_SELECTION: grid->dataSet gridSelected={grid.SelectedItems.Count} " +
                              $"selectedEntries={Describe(selectionDataSet.SelectedEntries)} " +
                              $"current={(selectionDataSet.Current as DataModel.TaskList)?.Name ?? "<null>"}");

            var partialSelectionPath = Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT") + ".partial-selection.png";
            for (var i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            using (var partialFrame = lastOpenedWindow.CaptureRenderedFrame())
                partialFrame?.Save(partialSelectionPath);
            Console.WriteLine($"KAPOK_SELECTION: saved partial-selection screenshot to {partialSelectionPath}");

            // The actual contract this sync exists for: the real DeleteEntryAction (an
            // IDataSetSelectionAction<TaskList>) has to accept the list as-is. This is what would
            // throw an InvalidCastException if the snapshot were a List<object>.
            Console.WriteLine($"KAPOK_SELECTION: deleteEntryAction.CanExecute(selectedEntries)=" +
                              $"{selectionDataSet.DeleteEntryAction.CanExecute(selectionDataSet.SelectedEntries)}");

            // Direction 2: DataSet.SelectedEntries -> grid selection, driven by the real
            // DataSet.SelectAllAction (which assigns Collection.ToList()).
            var selectAllAction = (IAction)selectionDataSet.GetType().GetProperty("SelectAllAction")!.GetValue(selectionDataSet)!;
            selectAllAction.Execute();
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_SELECTION: dataSet->grid (SelectAllAction) gridSelected=" +
                              $"[{string.Join(", ", grid.SelectedItems.Cast<DataModel.TaskList>().Select(t => t.Name))}]");

            // And the same thing through the real Ctrl+A KeyBinding ListPageView installs, rather
            // than by calling the action directly - proves the shortcut is actually wired.
            grid.SelectedItems.Clear();
            Dispatcher.UIThread.RunJobs();
            grid.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.A,
                KeyModifiers = KeyModifiers.Control
            });
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_SELECTION: ctrl+A gridSelected={grid.SelectedItems.Count} " +
                              $"selectedEntries={Describe(selectionDataSet.SelectedEntries)}");
            DumpRenderedCells("after ctrl+A");

            selectionDone: ;
        }

        // Phase 7 item 3 verification: per-column filters. Exercises the whole chain a user would
        // drive - the page's real ToggleFilterVisibleAction to show the filter row, then typing a
        // filter expression into one column's input and committing it - and prints the resulting
        // DataSet content and user filter layer, since "the grid shows fewer rows" is only
        // convincing if the filter object and the collection both actually changed.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_FILTER") == "1" &&
            page is Tasks tasksPage && tasksPage.DataSet is { } filterDataSet)
        {
            foreach (var taskName in new[] { "Buy milk", "Buy bread", "Call plumber" })
            {
                filterDataSet.CreateNewEntryAction.Execute();
                if (filterDataSet.Current is { } newTask)
                {
                    newTask.Name = taskName;
                    newTask.EstimatedTime = taskName.Length / 10m;
                }
            }
            filterDataSet.Save();
            Dispatcher.UIThread.RunJobs();

            string Rows() => string.Join(", ", filterDataSet.Collection.Select(t => t.Name));
            string FilterState() => $"userLayerProperties=[{string.Join(", ", filterDataSet.Filter.UserLayer.Properties.Select(f => f.PropertyInfo.Name + "=" + ((f as Kapok.BusinessLayer.IPropertyFilterStringFilter)?.FilterString ?? "?")))}]";

            Console.WriteLine($"KAPOK_FILTER: seeded rows=[{Rows()}] {FilterState()}");

            // The real toggle the Ribbon's "Filter" button is bound to - not IsFilterVisible set
            // directly - so this proves the whole page-level path, not just the grid property.
            tasksPage.ToggleFilterVisibleAction.Execute();
            Dispatcher.UIThread.RunJobs();

            var filterGrid = lastOpenedWindow!.GetVisualDescendants().OfType<CustomDataGrid>().First();
            var filterControls = lastOpenedWindow.GetVisualDescendants().OfType<DataGridColumnFilter>().ToList();
            Console.WriteLine($"KAPOK_FILTER: isFilterVisible={filterGrid.IsFilterVisible} columnHeaderHeight={filterGrid.ColumnHeaderHeight} filterInputs=" +
                              $"[{string.Join(", ", filterControls.Select(f => (f.ColumnFilter?.PropertyBindingPath ?? "<no vm>") + (f.CanUserFilter ? "" : " (not filterable)") + $" visible={f.IsVisible} bounds={f.Bounds}"))}]");
            Console.WriteLine($"KAPOK_FILTER: headers=[{string.Join(", ", lastOpenedWindow.GetVisualDescendants().OfType<DataGridColumnHeader>().Select(h => h.Bounds.ToString()))}]");

            for (var i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            var filterRowPath = Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT") + ".filter-row.png";
            using (var frame = lastOpenedWindow.CaptureRenderedFrame())
                frame?.Save(filterRowPath);
            Console.WriteLine($"KAPOK_FILTER: saved filter-row screenshot to {filterRowPath}");

            var nameFilter = filterControls.FirstOrDefault(f => f.ColumnFilter?.PropertyBindingPath == nameof(DataModel.Task.Name));
            if (nameFilter?.ColumnFilter == null)
            {
                Console.WriteLine("KAPOK_FILTER: no filter input found for Task.Name");
            }
            else
            {
                // A real filter expression in Kapok's own filter-string syntax ('*' -> SQL LIKE).
                nameFilter.ColumnFilter.QueryString = "Buy*";
                nameFilter.ApplyFilter();
                Dispatcher.UIThread.RunJobs();
                Console.WriteLine($"KAPOK_FILTER: after \"Buy*\" rows=[{Rows()}] {FilterState()}");

                // An expression the parser cannot make sense of for a decimal column - proves the
                // validation path (INotifyDataErrorInfo -> the input's error tooltip) is live, not
                // just the happy path.
                var estimatedTimeFilter = filterControls.FirstOrDefault(f => f.ColumnFilter?.PropertyBindingPath == nameof(DataModel.Task.EstimatedTime));
                if (estimatedTimeFilter?.ColumnFilter != null)
                {
                    estimatedTimeFilter.ColumnFilter.QueryString = "not-a-number";
                    estimatedTimeFilter.ApplyFilter();
                    Dispatcher.UIThread.RunJobs();
                    var errors = estimatedTimeFilter.ColumnFilter.GetErrors(nameof(DataGridColumnFilterViewModel.QueryString))
                        .Cast<object>().Select(e => (e as Kapok.BusinessLayer.BusinessLayerMessage)?.Text ?? e.ToString()).ToList();
                    Console.WriteLine($"KAPOK_FILTER: invalid expression hasErrors={estimatedTimeFilter.ColumnFilter.HasErrors} errors=[{string.Join(" / ", errors)}]");
                    estimatedTimeFilter.ColumnFilter.QueryString = string.Empty;
                    estimatedTimeFilter.ApplyFilter();
                    Dispatcher.UIThread.RunJobs();
                }

                for (var i = 0; i < 3; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }
                var filteredPath = Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT") + ".filtered.png";
                using (var frame = lastOpenedWindow.CaptureRenderedFrame())
                    frame?.Save(filteredPath);
                Console.WriteLine($"KAPOK_FILTER: saved filtered screenshot to {filteredPath}");

                // Clearing the input has to remove the property filter again, not just blank the box.
                nameFilter.ColumnFilter.QueryString = string.Empty;
                nameFilter.ApplyFilter();
                Dispatcher.UIThread.RunJobs();
                Console.WriteLine($"KAPOK_FILTER: after clearing rows=[{Rows()}] {FilterState()}");

                // A filter that already exists on the user layer but is NOT a string filter (here
                // a PropertyStaticFilter, the shape application code sets programmatically). Two
                // ported branches only this exercises: SetQueryStringFromProperty rendering it
                // back through IPropertyFilter.AsFilterString, and UpdateFilter replacing it with
                // an equivalent string filter (IFilterSet.ReplacePropertyFilter) once the user
                // types over it.
                var priorityFilter = filterControls.FirstOrDefault(f => f.ColumnFilter?.PropertyBindingPath == nameof(DataModel.Task.Priority));
                if (priorityFilter?.ColumnFilter != null)
                {
                    filterDataSet.Filter.UserLayer.Properties.Add(
                        new Kapok.BusinessLayer.PropertyStaticFilter<DataModel.Task>(nameof(DataModel.Task.Priority))
                        {
                            FilterValue = DataModel.TaskPriority.Normal
                        });
                    Dispatcher.UIThread.RunJobs();
                    Console.WriteLine($"KAPOK_FILTER: static filter shown as \"{priorityFilter.ColumnFilter.QueryString}\" " +
                                      $"readOnly={priorityFilter.ColumnFilter.IsReadOnly} rows=[{Rows()}] " +
                                      $"filterTypes=[{string.Join(", ", filterDataSet.Filter.UserLayer.Properties.Select(f => f.GetType().Name))}]");

                    priorityFilter.ColumnFilter.QueryString = "High";
                    priorityFilter.ApplyFilter();
                    Dispatcher.UIThread.RunJobs();
                    Console.WriteLine($"KAPOK_FILTER: after typing over it rows=[{Rows()}] {FilterState()} " +
                                      $"filterTypes=[{string.Join(", ", filterDataSet.Filter.UserLayer.Properties.Select(f => f.GetType().Name))}]");

                    priorityFilter.ColumnFilter.QueryString = string.Empty;
                    priorityFilter.ApplyFilter();
                    Dispatcher.UIThread.RunJobs();
                    Console.WriteLine($"KAPOK_FILTER: after clearing it rows=[{Rows()}] {FilterState()}");
                }
            }
        }

        // Phase 7 item 4 verification: the drill-down column's link actually runs the DataSet's
        // drill-down action. A screenshot can show a blue underlined cell; only executing the cell's
        // real command proves it opens the referenced page filtered to the selected entries.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_DRILLDOWN") == "1" &&
            page is TaskLists drillDownPage && drillDownPage.DataSet is { } drillDownDataSet)
        {
            // Two lists, and one Task in the first - so a drill-down that ignored the filter would
            // be visibly different from one that applied it.
            drillDownDataSet.CreateNewEntryAction.Execute();
            var groceries = drillDownDataSet.Current!;
            groceries.Name = "Groceries";
            drillDownDataSet.CreateNewEntryAction.Execute();
            drillDownDataSet.Current!.Name = "Hardware";
            drillDownDataSet.Save();

            var tasksDataSet = GetService<Tasks>().DataSet!;
            tasksDataSet.CreateNewEntryAction.Execute();
            tasksDataSet.Current!.Name = "Buy milk";
            tasksDataSet.Current.TaskListId = groceries.Id;
            tasksDataSet.Save();

            Dispatcher.UIThread.RunJobs();

            var drillGrid = lastOpenedWindow!.GetVisualDescendants().OfType<CustomDataGrid>().First();
            drillGrid.SelectedItems.Clear();
            drillGrid.SelectedItems.Add(drillGrid.ItemsSource!.Cast<object>().First(o => ((DataModel.TaskList)o).Name == "Groceries"));
            Dispatcher.UIThread.RunJobs();

            // The link is the Button the DataGridHyperlinkCommandColumn's cell template builds.
            var linkButton = drillGrid.GetVisualDescendants().OfType<DataGridCell>()
                .SelectMany(c => c.GetVisualDescendants().OfType<Button>())
                .FirstOrDefault();

            Console.WriteLine($"KAPOK_DRILLDOWN: link found={linkButton != null} " +
                              $"canExecute={linkButton?.Command?.CanExecute(linkButton.CommandParameter)} " +
                              $"parameter={(linkButton?.CommandParameter as System.Collections.IList)?.Count}");

            var windowsBefore = lastOpenedWindow;
            linkButton?.Command?.Execute(linkButton.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            var openedPage = (lastOpenedWindow == windowsBefore ? null : lastOpenedWindow?.DataContext) as IDataPage;
            var openedRows = openedPage?.DataSet is { } openedDataSet
                ? string.Join(", ", openedDataSet.AsQueryable().Cast<object>().ToList().Select(o => (o as DataModel.Task)?.Name))
                : "<no page opened>";
            Console.WriteLine($"KAPOK_DRILLDOWN: openedPage={openedPage?.GetType().Name} rows=[{openedRows}]");
        }

        // Phase 7 item 4 verification: the lookup column's *editing* template. The read-only cell
        // showing "Groceries" instead of a Guid is visible in the Tasks screenshot; this proves the
        // editor is a real LookupComboBox over the real lookup entries, bound to the row's key.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_LOOKUP_EDIT") == "1" &&
            page is Tasks lookupEditPage)
        {
            Dispatcher.UIThread.RunJobs();

            var lookupGrid = lastOpenedWindow!.GetVisualDescendants().OfType<CustomDataGrid>().First();
            var lookupColumn = lookupGrid.Columns.OfType<DataGridLookupComboBoxColumn>().FirstOrDefault();

            Console.WriteLine($"KAPOK_LOOKUP_EDIT: isEditable={lookupEditPage.IsEditable} gridReadOnly={lookupGrid.IsReadOnly} " +
                              $"lookupColumn={lookupColumn?.GetType().Name} columnReadOnly={lookupColumn?.IsReadOnly} " +
                              $"selectedValuePath={lookupColumn?.SelectedValuePath}");

            if (lookupColumn != null)
            {
                lookupGrid.SelectedIndex = 0;
                lookupGrid.CurrentColumn = lookupColumn;
                lookupGrid.BeginEdit();
                for (var i = 0; i < 3; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }

                var editor = lookupGrid.GetVisualDescendants().OfType<LookupComboBox>().FirstOrDefault();
                Console.WriteLine($"KAPOK_LOOKUP_EDIT: editor={editor?.GetType().Name} " +
                                  $"items=[{string.Join(", ", editor?.ItemsSource?.Cast<object>().Select(o => (o as DataModel.TaskList)?.Name) ?? [])}] " +
                                  $"selectedValue={editor?.SelectedValue} selectedItem={(editor?.SelectedItem as DataModel.TaskList)?.Name}");

                var editPath = Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT") + ".lookup-edit.png";
                using (var frame = lastOpenedWindow.CaptureRenderedFrame())
                    frame?.Save(editPath);
                Console.WriteLine($"KAPOK_LOOKUP_EDIT: saved screenshot to {editPath}");
            }
        }

        // Phase 7 item 5 verification: Excel-style paste. Two runs in one: a real clipboard
        // round-trip (put tab-separated text on the actual system clipboard, then Ctrl/Cmd+V on the
        // grid) and a direct PasteRows call. Both matter - the first proves the clipboard read and
        // the key binding, the second proves the row/column walk and value conversion deterministically,
        // without depending on a clipboard being available at all in the headless environment.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_PASTE") == "1" &&
            page is Tasks pastePage && pastePage.DataSet is { } pasteDataSet)
        {
            pasteDataSet.CreateNewEntryAction.Execute();
            pasteDataSet.Current!.Name = "Existing task";
            Dispatcher.UIThread.RunJobs();

            var pasteGrid = lastOpenedWindow!.GetVisualDescendants().OfType<CustomDataGrid>().First();
            pasteGrid.SelectedIndex = 0;
            pasteGrid.CurrentColumn = pasteGrid.Columns.First();
            Dispatcher.UIThread.RunJobs();

            string Tasks_() => string.Join(" | ", pasteDataSet.Collection.Select(t =>
                $"{t.Name}/{t.Priority}/{t.EstimatedTime}/{t.DueDate:yyyy-MM-dd}"));

            Console.WriteLine($"KAPOK_PASTE: before rows={pasteDataSet.Collection.Count} [{Tasks_()}]");
            Console.WriteLine($"KAPOK_PASTE: canUserPasteToNewRows={pasteGrid.CanUserPasteToNewRows} " +
                              $"gridReadOnly={pasteGrid.IsReadOnly}");

            // Three rows x four columns (Task / Priority / Description / Est. h), the first
            // overwriting the existing row and the other two creating new entries. Deliberately
            // exercises three different conversions: string, enum (by name) and decimal.
            var clipboardRows = new List<object?[]>
            {
                new object?[] { "Buy milk", "High", "Two litres", "1.25" },
                new object?[] { "Buy bread", "Normal", "Sourdough", "0.5" },
                new object?[] { "Call plumber", "Urgent", "Leaking tap", "2" }
            };

            var pastedCells = pasteGrid.PasteRows(clipboardRows);
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_PASTE: PasteRows wrote {pastedCells} cells, rows={pasteDataSet.Collection.Count} [{Tasks_()}]");

            // Same content, now through the real system clipboard and the real Ctrl/Cmd+V handler.
            var clipboard = lastOpenedWindow.Clipboard;
            if (clipboard != null)
            {
                var dataTransfer = new DataTransfer();
                dataTransfer.Add(DataTransferItem.CreateText("Wash car\tLow\tIncluding the wheels\t0.75"));
                clipboard.SetDataAsync(dataTransfer).GetAwaiter().GetResult();

                pasteGrid.SelectedIndex = pasteDataSet.Collection.Count - 1;
                pasteGrid.CurrentColumn = pasteGrid.Columns.First();
                Dispatcher.UIThread.RunJobs();

                pasteGrid.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.V,
                    KeyModifiers = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control
                });

                for (var i = 0; i < 5; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }

                Console.WriteLine($"KAPOK_PASTE: after Ctrl+V rows={pasteDataSet.Collection.Count} [{Tasks_()}]");
            }
            else
            {
                Console.WriteLine("KAPOK_PASTE: no clipboard available in this environment - Ctrl+V path not exercised");
            }

            // CanUserPasteToNewRows=false must stop at the existing rows instead of growing the list.
            pasteGrid.CanUserPasteToNewRows = false;
            var rowsBefore = pasteDataSet.Collection.Count;
            pasteGrid.SelectedIndex = rowsBefore - 1;
            pasteGrid.CurrentColumn = pasteGrid.Columns.First();
            Dispatcher.UIThread.RunJobs();
            pasteGrid.PasteRows(new List<object?[]>
            {
                new object?[] { "Overwrites last row" },
                new object?[] { "Must not be created" }
            });
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_PASTE: with CanUserPasteToNewRows=false rows={pasteDataSet.Collection.Count} (was {rowsBefore}) [{Tasks_()}]");
        }

        // Phase 7 item 6 verification: drag & drop row reordering. Exercised on TaskCategories,
        // the one showcase entity implementing ISortableEntity (which is what makes the grid offer
        // the drag at all). Two halves: the pointer gesture itself - press, move past the drag
        // threshold, release - and the resulting collection order plus the SortOrder values written
        // back onto the entities, since a reorder that is not persisted is not a reorder.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_ROW_DRAG") == "1" &&
            page is TaskCategories && lastOpenedWindow != null)
        {
            Dispatcher.UIThread.RunJobs();

            var dragGrid = lastOpenedWindow.GetVisualDescendants().OfType<CustomDataGrid>().First();
            var rows = dragGrid.ItemsSource!.Cast<DataModel.TaskCategory>().ToList();

            string Order() => string.Join(", ", dragGrid.ItemsSource!.Cast<DataModel.TaskCategory>()
                .Select(c => $"{c.Name}#{c.SortOrder}"));

            Console.WriteLine($"KAPOK_ROW_DRAG: canUserReorderRows={dragGrid.CanUserReorderRows} before=[{Order()}]");

            // The gesture. Directly-raised pointer events rather than AvaloniaHeadless's
            // MouseDown/MouseMove/MouseUp - the proven-reliable pattern from Phase 5, whose
            // simulated pointer input never reached targets nested inside DockPageWindow's
            // Dock.Avalonia chrome.
            var realizedRows = dragGrid.GetVisualDescendants().OfType<DataGridRow>().OrderBy(r => r.Bounds.Y).ToList();
            var sourceRow = realizedRows.First(r => ((DataModel.TaskCategory)r.DataContext!).Name == "Work");
            var targetRow = realizedRows.First(r => ((DataModel.TaskCategory)r.DataContext!).Name == "Home");

            // Positions are given relative to the window, which is also what is passed as the
            // event's root visual - PointerEventArgs.GetPosition translates from there, so a
            // grid-relative point paired with the grid as root visual comes back skewed (seen
            // during this item's verification: a press meant for a row landed at y=-45).
            Point CentreOf(DataGridRow row) =>
                row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), lastOpenedWindow) ?? default;

            var start = CentreOf(sourceRow);
            var end = CentreOf(targetRow);

            var pointer = new global::Avalonia.Input.Pointer(0, PointerType.Mouse, isPrimary: true);
            var pressProperties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);

            sourceRow.RaiseEvent(new PointerPressedEventArgs(sourceRow, pointer, lastOpenedWindow, start,
                0, pressProperties, KeyModifiers.None));
            Console.WriteLine($"KAPOK_ROW_DRAG: after press draggedItem={(dragGrid.DraggedItem as DataModel.TaskCategory)?.Name}");

            targetRow.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, targetRow, pointer,
                lastOpenedWindow, end, 0, pressProperties, KeyModifiers.None));
            // The drag ghost's own rendering cannot be asserted here: opening any Popup inside this
            // port's RibbonWindow fails under Avalonia.Headless (the limitation Phase 5 documented
            // for LookupComboBox's dropdown), and CustomDataGrid deliberately swallows that so the
            // reorder still works. What is checked is that the ghost was created and that the grid
            // switched itself read-only for the duration of the drag.
            Console.WriteLine($"KAPOK_ROW_DRAG: during drag dragPopupExists={dragGrid.DragPopup != null} gridReadOnly={dragGrid.IsReadOnly}");

            targetRow.RaiseEvent(new PointerReleasedEventArgs(targetRow, pointer, lastOpenedWindow, end,
                0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                KeyModifiers.None, MouseButton.Left));

            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_ROW_DRAG: after drop draggedItem={(dragGrid.DraggedItem as DataModel.TaskCategory)?.Name ?? "<null>"} " +
                              $"dragPopupExists={dragGrid.DragPopup != null} gridReadOnly={dragGrid.IsReadOnly} " +
                              $"selected={(dragGrid.SelectedItem as DataModel.TaskCategory)?.Name}");
            Console.WriteLine($"KAPOK_ROW_DRAG: after=[{Order()}]");

            // And the operation on its own, so the reorder is verified independently of whatever
            // headless pointer routing does.
            dragGrid.MoveRow(rows.First(c => c.Name == "Groceries"), rows.First(c => c.Name == "Garden"));
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_ROW_DRAG: after MoveRow(Groceries -> Garden)=[{Order()}]");
        }

        // Phase 8 item 4 verification: hierarchy *navigation* (as opposed to Phase 7 item 4's tree
        // *column*, which only ever renders whatever Level/HasChildren/IsExpanded already say).
        // Exercises AvaloniaHierarchyDataSetView's real Collapse/Expand/MoveOut/MoveIn actions -
        // the same TaskCategories tree ROW_DRAG uses (Home/Kitchen/Groceries/Garden/Work), and
        // prints the resulting visible Collection order plus each entry's
        // Level/IsVisible/IsExpanded/Parent after every step, since a screenshot alone can't prove
        // *why* a row disappeared or which entry became whose parent.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_HIERARCHY_NAV") == "1" &&
            page is TaskCategories categoriesPage &&
            categoriesPage.DataSet is IHierarchyDataSetView<DataModel.TaskCategory> hierarchyDataSet)
        {
            Dispatcher.UIThread.RunJobs();

            string Rows() => string.Join(", ", hierarchyDataSet.Collection
                .Select(c => $"{c.Name}(L{c.Level}{(c.HasChildren ? (c.IsExpanded ? "-v" : "-collapsed") : "")})"));

            DataModel.TaskCategory Find(string name) => hierarchyDataSet.Collection.First(c => c.Name == name);

            Console.WriteLine($"KAPOK_HIERARCHY_NAV: initial=[{Rows()}]");

            // Collapse "Home" - its descendants (Kitchen, Groceries) must disappear from the
            // visible Collection, "Garden"/"Work" (not descendants) must not be affected.
            hierarchyDataSet.Current = Find("Home");
            Console.WriteLine($"KAPOK_HIERARCHY_NAV: CollapseAction.CanExecute={hierarchyDataSet.CollapseAction.CanExecute()}");
            hierarchyDataSet.CollapseAction.Execute();
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_HIERARCHY_NAV: after collapsing Home=[{Rows()}] " +
                              $"homeIsExpanded={Find("Home").IsExpanded} " +
                              $"current={hierarchyDataSet.Current?.Name ?? "<null>"}");

            // Expand it again via ToggleAction (not ExpandAction directly) - proves the combined
            // expand/collapse toggle also works, not just the two dedicated actions.
            Console.WriteLine($"KAPOK_HIERARCHY_NAV: ToggleAction.CanExecute={hierarchyDataSet.ToggleAction.CanExecute()}");
            hierarchyDataSet.ToggleAction.Execute();
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_HIERARCHY_NAV: after toggling Home=[{Rows()}]");

            // Same for "Kitchen" (nested one level deeper) - collapsing it must hide "Groceries"
            // without touching "Home" itself.
            hierarchyDataSet.Current = Find("Kitchen");
            hierarchyDataSet.CollapseAction.Execute();
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_HIERARCHY_NAV: after collapsing Kitchen=[{Rows()}]");
            hierarchyDataSet.ExpandAction.Execute();
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_HIERARCHY_NAV: after expanding Kitchen=[{Rows()}]");

            // MoveOut: "Garden" (level 1, child of Home) becomes a child of "Groceries"'s parent
            // slot - the nearest preceding same-level entry is "Kitchen" (also level 1), so Garden
            // should become Kitchen's child (level 2, parent=Kitchen).
            hierarchyDataSet.Current = Find("Garden");
            Console.WriteLine($"KAPOK_HIERARCHY_NAV: MoveOutAction.CanExecute(Garden)={hierarchyDataSet.MoveOutAction.CanExecute()}");
            hierarchyDataSet.MoveOutAction.Execute();
            Dispatcher.UIThread.RunJobs();
            var garden = Find("Garden");
            Console.WriteLine($"KAPOK_HIERARCHY_NAV: after MoveOut(Garden)=[{Rows()}] " +
                              $"gardenLevel={garden.Level} gardenParent={garden.Parent?.Name ?? "<null>"} " +
                              $"gardenParentId={garden.ParentId} kitchenId={Find("Kitchen").Id} " +
                              $"kitchenHasChildren={Find("Kitchen").HasChildren}");

            // MoveIn: promote Garden straight back out to level 0 (Kitchen -> Home -> root, i.e.
            // MoveIn once should land it back at level 1 under Home again since MoveIn only ever
            // steps up by one level and detaches the parent link entirely - a second MoveIn would
            // be needed to reach level 0).
            Console.WriteLine($"KAPOK_HIERARCHY_NAV: MoveInAction.CanExecute(Garden)={hierarchyDataSet.MoveInAction.CanExecute()}");
            hierarchyDataSet.MoveInAction.Execute();
            Dispatcher.UIThread.RunJobs();
            garden = Find("Garden");
            Console.WriteLine($"KAPOK_HIERARCHY_NAV: after MoveIn(Garden)=[{Rows()}] " +
                              $"gardenLevel={garden.Level} gardenParent={garden.Parent?.Name ?? "<null>"} " +
                              $"gardenParentId={garden.ParentId}");
        }

        // Phase 7 item 7: a real side-by-side probe of Avalonia's *native* DataGrid cell navigation
        // before deciding whether WPF's Excel-navigation code needs porting at all. Prints what the
        // grid actually does for each key, rather than assuming.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_NAV") == "1" &&
            page is Tasks navPage && navPage.DataSet is { } navDataSet)
        {
            foreach (var taskName in new[] { "Row one", "Row two", "Row three" })
            {
                navDataSet.CreateNewEntryAction.Execute();
                navDataSet.Current!.Name = taskName;
            }
            Dispatcher.UIThread.RunJobs();

            var navGrid = lastOpenedWindow!.GetVisualDescendants().OfType<CustomDataGrid>().First();

            // Only the *current* cell counts: a cell that merely contains a ComboBox in its
            // template (the enum column) is not an open editor, so scanning the whole grid for one
            // reports false positives.
            string EditingCell()
            {
                // DataGridColumn.GetCellContent(item) returns the element the column generated for
                // that row; its owning DataGridCell is the one to inspect.
                var content = navGrid.SelectedItem == null || navGrid.CurrentColumn == null
                    ? null
                    : navGrid.CurrentColumn.GetCellContent(navGrid.SelectedItem);
                var cell = content?.GetSelfAndVisualAncestors().OfType<DataGridCell>().FirstOrDefault();
                if (cell == null)
                    return "<no cell>";

                var editor = cell.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (editor != null)
                    return $"TextBox:{editor.Text}";

                return cell.GetVisualDescendants().OfType<ComboBox>().Any() ? "ComboBox" : "<none>";
            }

            string State(string label) =>
                $"KAPOK_NAV: {label} row={(navGrid.SelectedItem as DataModel.Task)?.Name} " +
                $"column={navGrid.CurrentColumn?.Header} editingCell={EditingCell()}";

            void Key(Key key, KeyModifiers modifiers = KeyModifiers.None)
            {
                navGrid.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = key,
                    KeyModifiers = modifiers
                });
                Dispatcher.UIThread.RunJobs();
            }

            navGrid.SelectedIndex = 0;
            navGrid.CurrentColumn = navGrid.Columns[0];
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine(State("start, AutoBeginEditOnCurrentCell off (no auto-edit expected)"));

            // Auto-begin-edit is opt-in here, unlike WPF - see
            // CustomDataGrid.AutoBeginEditOnCurrentCellProperty for why.
            navGrid.AutoBeginEditOnCurrentCell = true;
            navGrid.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine(State("with AutoBeginEditOnCurrentCell on, after moving the current cell"));
            navGrid.AutoBeginEditOnCurrentCell = false;
            navGrid.CancelEdit();
            navGrid.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();

            Key(global::Avalonia.Input.Key.Down);
            Console.WriteLine(State("after Down"));

            Key(global::Avalonia.Input.Key.Up);
            Console.WriteLine(State("after Up"));

            navGrid.BeginEdit();
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine(State("after BeginEdit"));

            Key(global::Avalonia.Input.Key.Enter);
            Console.WriteLine(State("after Enter while editing"));

            navGrid.BeginEdit();
            Dispatcher.UIThread.RunJobs();
            Key(global::Avalonia.Input.Key.Escape);
            Console.WriteLine(State("after Escape while editing"));

            Key(global::Avalonia.Input.Key.Tab);
            Console.WriteLine(State("after Tab"));

            // Excel navigation proper: Enter while editing should walk right across editable
            // columns and then wrap to the next row, not just drop down a row.
            navGrid.SelectedIndex = 0;
            navGrid.CurrentColumn = navGrid.Columns[0];
            navGrid.BeginEdit();
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine(State("excel: begin edit at first cell"));

            for (var i = 1; i <= 6; i++)
            {
                Key(global::Avalonia.Input.Key.Enter);
                Console.WriteLine(State($"excel: after Enter #{i}"));
            }

            // Up/Down while editing must stay in edit mode on the new row.
            Key(global::Avalonia.Input.Key.Down);
            Console.WriteLine(State("excel: after Down while editing"));
            Key(global::Avalonia.Input.Key.Up);
            Console.WriteLine(State("excel: after Up while editing"));

            // And PauseExcelNavigation must hand the keys back to the cell's own editor.
            // With the pause on, the Excel handler must not claim the key - it goes to the base
            // DataGrid (or, in a real app, to the open dropdown that set the pause). The observable
            // difference is the *column*: Excel navigation walks right, the native handler does not.
            navGrid.PauseExcelNavigation = true;
            var pausedColumn = navGrid.CurrentColumn?.Header;
            Key(global::Avalonia.Input.Key.Enter);
            Console.WriteLine($"KAPOK_NAV: paused: column before={pausedColumn} after={navGrid.CurrentColumn?.Header} " +
                              $"(unchanged={Equals(navGrid.CurrentColumn?.Header, pausedColumn)}) " +
                              $"row={(navGrid.SelectedItem as DataModel.Task)?.Name}");
            navGrid.PauseExcelNavigation = false;
        }

        // DataGridStyling audit verification: per-entity row colouring and row activation
        // (double-click), the two genuinely functional pieces of WPF's DataGridStyling.xaml.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_ROW_STYLE") == "1" &&
            page is Tasks styledPage && styledPage.DataSet is { } styledDataSet)
        {
            // Not editable, so a double-click opens the card page instead of editing in place -
            // exactly the condition WPF's ListControlEntryMouseDoubleClickCommand checks.
            styledPage.Editable = false;

            var seedRows = new (string Name, DataModel.TaskPriority Priority, DateTime? Due)[]
            {
                ("Normal task", DataModel.TaskPriority.Normal, null),
                ("Urgent task", DataModel.TaskPriority.Urgent, null),
                ("Overdue task", DataModel.TaskPriority.Low, new DateTime(2020, 1, 1))
            };
            foreach (var (taskName, priority, due) in seedRows)
            {
                styledDataSet.CreateNewEntryAction.Execute();
                styledDataSet.Current!.Name = taskName;
                styledDataSet.Current.Priority = priority;
                styledDataSet.Current.DueDate = due;
            }

            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }

            var styledGrid = lastOpenedWindow!.GetVisualDescendants().OfType<CustomDataGrid>().First();
            Console.WriteLine($"KAPOK_ROW_STYLE: coloringDataSet={styledGrid.ColoringDataSet?.GetType().Name} " +
                              $"isEditable={styledPage.IsEditable} gridReadOnly={styledGrid.IsReadOnly}");
            foreach (var styledRow in styledGrid.GetVisualDescendants().OfType<DataGridRow>().OrderBy(r => r.Bounds.Y))
            {
                Console.WriteLine($"KAPOK_ROW_STYLE:   row={(styledRow.DataContext as DataModel.Task)?.Name} " +
                                  $"background={(styledRow.Background as global::Avalonia.Media.SolidColorBrush)?.Color.ToString() ?? "<none>"}");
            }

            var rowColorsPath = Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT") + ".row-colors.png";
            using (var frame = lastOpenedWindow.CaptureRenderedFrame())
                frame?.Save(rowColorsPath);
            Console.WriteLine($"KAPOK_ROW_STYLE: saved row-colours screenshot to {rowColorsPath}");

            // Row activation: a double-click on a row must open its card page.
            var activateRow = styledGrid.GetVisualDescendants().OfType<DataGridRow>()
                .First(r => ((DataModel.Task)r.DataContext!).Name == "Urgent task");
            var windowBefore = lastOpenedWindow;
            var activatePoint = activateRow.TranslatePoint(new Point(20, activateRow.Bounds.Height / 2), lastOpenedWindow) ?? default;

            activateRow.RaiseEvent(new PointerPressedEventArgs(activateRow,
                new global::Avalonia.Input.Pointer(0, PointerType.Mouse, isPrimary: true),
                lastOpenedWindow, activatePoint, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, clickCount: 2));

            Dispatcher.UIThread.RunJobs();
            var activatedPage = lastOpenedWindow == windowBefore ? null : lastOpenedWindow?.DataContext;
            Console.WriteLine($"KAPOK_ROW_STYLE: double-click openedPage={activatedPage?.GetType().Name ?? "<none>"} " +
                              $"entry={((activatedPage as IDataPage)?.DataSet?.Current as DataModel.Task)?.Name}");
        }

        // Phase 8 item 6 verification: ListPageView's own toolbar (sort ascending/descending,
        // list-view selector) - real UI wired to DataSet.SortAscendingAction/SortDescendingAction
        // and the page's own ListViews/CurrentListView, none of which had any UI to reach them
        // before this. Finds the real Button controls ListPageView built (by Name, see
        // ListPageView.BuildToolbar) and drives them exactly like a user would - Command.Execute(),
        // not the underlying DataSet action directly - so this proves the *wiring*, not just that
        // the already-known-working actions still work on their own.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_LIST_TOOLBAR") == "1" &&
            page is Tasks toolbarPage && toolbarPage.DataSet is { } toolbarDataSet)
        {
            foreach (var taskName in new[] { "Charlie", "Alpha", "Bravo" })
            {
                toolbarDataSet.CreateNewEntryAction.Execute();
                toolbarDataSet.Current!.Name = taskName;
            }
            // SortAscendingAction/SortDescendingAction and the list-view selector both call
            // DataSet.Refresh() (see DataSetView<TEntry>.SortAscending/ListPage.OnCurrentListViewChanged),
            // a real server-side re-query - saved first so the seeded rows still exist afterward,
            // and specifically to avoid the already-documented EntityDeferredCommitService deadlock
            // (a Load/Refresh against a DataSet holding an uncommitted entity with an FK navigation
            // property - see this file's own KAPOK_HEADLESS_SCREENSHOT_LIST_VIEW comment) that an
            // unsaved Task (it has a TaskListId FK) would otherwise hit here.
            toolbarDataSet.Save();
            Dispatcher.UIThread.RunJobs();

            string Rows() => string.Join(", ", toolbarDataSet.Collection.Select(t => t.Name));

            var sortAscendingButton = lastOpenedWindow!.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.Name == "SortAscendingButton");
            var sortDescendingButton = lastOpenedWindow.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.Name == "SortDescendingButton");
            var listViewButton = lastOpenedWindow.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.Name == "ListViewButton");

            Console.WriteLine($"KAPOK_LIST_TOOLBAR: canUserSort={toolbarDataSet.CanUserSort} " +
                              $"sortAscendingFound={sortAscendingButton != null} isVisible={sortAscendingButton?.IsVisible} " +
                              $"sortDescendingFound={sortDescendingButton != null} isVisible={sortDescendingButton?.IsVisible} " +
                              $"listViewFound={listViewButton != null}");
            Console.WriteLine($"KAPOK_LIST_TOOLBAR: before sorting=[{Rows()}]");

            sortAscendingButton?.Command?.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_LIST_TOOLBAR: after clicking sort-ascending=[{Rows()}] " +
                              $"sortDirection={toolbarDataSet.SortDirection}");

            sortDescendingButton?.Command?.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_LIST_TOOLBAR: after clicking sort-descending=[{Rows()}] " +
                              $"sortDirection={toolbarDataSet.SortDirection}");

            var listPageView = lastOpenedWindow.GetVisualDescendants()
                .OfType<Kapok.View.Avalonia.DefaultPageControls.ListPageView>().FirstOrDefault();
            var flyout = (listViewButton?.Flyout as global::Avalonia.Controls.MenuFlyout);
            var menuItems = flyout?.Items.OfType<MenuItem>().ToList();
            Console.WriteLine($"KAPOK_LIST_TOOLBAR: listPageViewFound={listPageView != null} " +
                              $"menuItemCount={menuItems?.Count} " +
                              $"menuItemHeaders=[{string.Join(", ", menuItems?.Select(m => m.Header) ?? [])}] " +
                              $"currentListView={toolbarPage.CurrentListView?.Name}");

            var withDueDateItem = menuItems?.FirstOrDefault(m => m.Header?.ToString() == "With due date");
            Console.WriteLine($"KAPOK_LIST_TOOLBAR: 'With due date' menuItem found={withDueDateItem != null} " +
                              $"canExecute={withDueDateItem?.Command?.CanExecute(null)}");
            withDueDateItem?.Command?.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"KAPOK_LIST_TOOLBAR: after selecting 'With due date' currentListView={toolbarPage.CurrentListView?.Name} " +
                              $"columns=[{string.Join(", ", toolbarDataSet.Columns.Select(c => c.Name))}]");
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

            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }

            // Phase 6 finding, not yet resolved: itemsCount confirms the dropdown DataGrid's
            // ItemsSource genuinely has the seeded "Groceries" TaskList (LookupComboBox itself
            // works), but columns/rows stay at 0 - AutoGeneratingColumn never fires here, even
            // though the structurally-identical ListPageView DataGrid (not popup-hosted) generates
            // columns correctly from the exact same AutoGenerateColumns=true mechanism elsewhere in
            // this same headless run. Tried and ruled out: more RunJobs()/render-tick passes (up to
            // 30), an explicit low DispatcherPriority, and setting ItemsSource via a direct
            // property assignment instead of Bind() - none changed the result. The one variable
            // that differs from the working ListPageView case is that this DataGrid is Popup.Child,
            // opened only via the KAPOK_HEADLESS_SCREENSHOT_OPEN_LOOKUP-only
            // EnablePopupOverlayLayer reflection hack a few lines up (real desktop backends never
            // take this path - see that hack's own comment) - so this is most likely another
            // symptom of the same headless-only limitation, not a LookupComboBox defect, but that
            // can't be conclusively confirmed without a real display to compare against (this dev
            // Mac's display sleeps when unattended - see the porting plan's Handoff). Left as an
            // open, honestly-documented gap rather than a claimed fix; see the plan file.
            var lookupGrid = lastOpenedWindow?.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault();
            Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_OPEN_LOOKUP: itemsCount={lookupComboBox?.ItemsSource?.Cast<object>().Count()} " +
                               $"columns={lookupGrid?.Columns.Count} rows={lookupGrid?.GetVisualDescendants().OfType<DataGridRow>().Count()}");

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

        // Phase 5 verification: proves UIElementDropBehavior actually forwards a dropped file to
        // TaskCard.DropFile (via IDropTargetOnPage), not just that DragDrop.SetAllowDrop was
        // called. Point-based simulation via Avalonia.Headless's TopLevel.DragDrop(point, ...) -
        // confirmed working for a plain Border in a plain Window in isolation - never reached the
        // target here, even with a geometrically correct on-screen point; the difference is
        // DockPageWindow's real Dock.Avalonia DockControl chrome between the window and
        // TaskCardView, which apparently doesn't route simulated raw input the way it would a
        // real platform event. Raising the routed DragOver/Drop events directly on the drop
        // target sidesteps that - same fallback already used for PopupThumbResizeBehavior's
        // Thumb.DragDelta above, and it's still the exact event UIElementDropBehavior listens to.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_DROP_FILE") == "1")
        {
            Dispatcher.UIThread.RunJobs();

            // IStorageItem can't be implemented by user code (Avalonia's analyzer rejects it -
            // confirmed the hard way: CS0535 naming the interface "not implementable by user
            // code"), so a real temp file + the window's own real StorageProvider is used to get
            // a genuine IStorageItem instead of a hand-rolled test double.
            var tempFilePath = Path.Combine(Path.GetTempPath(), "receipt.pdf");
            File.WriteAllText(tempFilePath, "test");
            var storageFile = lastOpenedWindow!.StorageProvider.TryGetFileFromPathAsync(new Uri(tempFilePath)).GetAwaiter().GetResult();

            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.CreateFile(storageFile!));

            var grid = lastOpenedWindow.GetVisualDescendants().OfType<Grid>()
                .FirstOrDefault(g => DragDrop.GetAllowDrop(g));
            if (grid != null)
            {
                var dropPoint = grid.TranslatePoint(new Point(grid.Bounds.Width / 2, grid.Bounds.Height / 2), lastOpenedWindow) ?? default;
                grid.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent, dataTransfer, grid, dropPoint, KeyModifiers.None));
                grid.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, dataTransfer, grid, dropPoint, KeyModifiers.None));
            }

            var taskCard = lastOpenedWindow.DataContext as TaskCard;
            Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_DROP_FILE: Task.Description={taskCard?.DataSet?.Current?.Description}");
        }

        // Phase 5 verification: proves TaskLists.ReportAction really opens a working
        // MimeTypeReportPageWindow (real mime-type list, real ReportParameterList) and that
        // SaveAsFileAction genuinely exports data via the registered TaskListsReportProcessor.
        // ViewDomain.ShowDialogPage blocks the calling thread with its own nested
        // Dispatcher.UIThread.PushFrame until the dialog closes (see AvaloniaViewDomain's own
        // comment on this - "keep the UI responsive" is the whole point), so triggering the
        // action directly here would hang forever with nothing to close it. Posting both the
        // "open" and "capture, save, save-as-file, close" steps lets the second one run *while*
        // the first's nested frame is pumping, the same way real queued UI work keeps running
        // behind an open modal dialog.
        if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_REPORT") == "1" &&
            page is TaskLists taskListsPage)
        {
            // Same deferred-commit visibility rule already found in Phase 5 item 1 (TaskCard's
            // lookup): a seeded-but-unsaved TaskList lives only in this page's own DataSet, not
            // yet the actual data store, so TaskListsReportProcessor's own separate
            // IDataDomainScope wouldn't see it without this. Named explicitly (KAPOK_HEADLESS_
            // SCREENSHOT_SEED alone leaves Name empty) so the exported report has a real,
            // recognizable value to show, not just a structurally-correct blank row.
            if (taskListsPage.DataSet?.Current != null)
                taskListsPage.DataSet.Current.Name = "Groceries";
            taskListsPage.DataSet?.Save();

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // Calls ViewDomain.OpenReportDialog directly rather than through
                    // taskListsPage.ReportAction.Execute() - UIAction.Execute() has its own
                    // internal try/catch that logs (via NLog, not visible in this console) and
                    // swallows the exception rather than rethrowing it, which would otherwise
                    // hide exactly the failure this debug path exists to surface.
                    GetService<IViewDomain>().OpenReportDialog(new ToDoAvaloniaApp.Report.TaskListsReport(), null, taskListsPage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_REPORT: OpenReportDialog threw: {ex}");
                }
            });
            Dispatcher.UIThread.Post(() =>
            {
                var reportWindow = lastOpenedWindow;
                var reportPage = reportWindow?.DataContext as MimeTypeReportPage;
                Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_REPORT: window={reportWindow?.GetType().Name} " +
                                   $"mimeTypes=[{string.Join(", ", reportPage?.SupportedMimeTypes.Select(m => m.DisplayName) ?? [])}] " +
                                   $"selected={reportPage?.SelectedMimeType?.DisplayName}");

                // Phase 5's Infralution.Localization.Wpf item: prints the actual resx-resolved
                // button captions and file-type label straight from the live rendered controls
                // (not the resx file directly) - proves ResxManager's ResourceManager lookup
                // really reached the controls under CultureInfo.CurrentUICulture (see
                // Program.cs's KAPOK_HEADLESS_SCREENSHOT_CULTURE), not just that the resx parses.
                var buttonCaptions = reportWindow?.GetVisualDescendants().OfType<Button>()
                    .Select(b => b.Content?.ToString()) ?? [];
                var fileTypeLabel = reportWindow?.GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Text?.EndsWith(":") == true)?.Text;
                Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_REPORT: culture={System.Globalization.CultureInfo.CurrentUICulture} " +
                                   $"buttons=[{string.Join(", ", buttonCaptions)}] fileTypeLabel=\"{fileTypeLabel}\"");

                // Phase 8 item 7 verification: ReportParameterList's ComboBox (proposal values)
                // and DatePicker (DateTime) editor branches, previously unverified because the
                // only parameter in this app was a plain bool (see TaskListsReport.cs's new
                // SortBy/GeneratedOn parameters). Finds the real rendered editor controls and
                // drives them exactly like a user would - setting SelectedItem/SelectedDate, not
                // touching ReportParameterViewModel.Value directly - so this proves the two-way
                // bindings ReportParameterList.BuildEditor wires up, not just that the view models
                // themselves compute proposal values correctly.
                var sortByComboBox = reportWindow?.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
                var generatedOnDatePicker = reportWindow?.GetVisualDescendants().OfType<DatePicker>().FirstOrDefault();
                Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_REPORT: sortByComboBox found={sortByComboBox != null} " +
                                   $"items=[{string.Join(", ", sortByComboBox?.ItemsSource?.Cast<object>() ?? [])}] " +
                                   $"selected={sortByComboBox?.SelectedItem} " +
                                   $"generatedOnDatePicker found={generatedOnDatePicker != null} " +
                                   $"selectedDate={generatedOnDatePicker?.SelectedDate}");

                if (sortByComboBox != null)
                {
                    sortByComboBox.SelectedItem = "IsArchived";
                    Dispatcher.UIThread.RunJobs();
                    Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_REPORT: after picking 'IsArchived' " +
                                       $"comboBoxSelected={sortByComboBox.SelectedItem} " +
                                       $"viewModelValue={(reportPage?.ReportParameters?.FirstOrDefault(p => p.ReportParameter.Name == "SortBy"))?.Value}");
                }

                if (generatedOnDatePicker != null)
                {
                    var pickedDate = new DateTimeOffset(new DateTime(2026, 1, 15));
                    generatedOnDatePicker.SelectedDate = pickedDate;
                    Dispatcher.UIThread.RunJobs();
                    Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_REPORT: after picking 2026-01-15 " +
                                       $"datePickerSelected={generatedOnDatePicker.SelectedDate} " +
                                       $"viewModelValue={(reportPage?.ReportParameters?.FirstOrDefault(p => p.ReportParameter.Name == "GeneratedOn"))?.Value}");
                }

                var reportScreenshotPath = Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT") + ".report.png";
                for (var i = 0; i < 3; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }
                using (var frame = reportWindow?.CaptureRenderedFrame())
                {
                    frame?.Save(reportScreenshotPath);
                    Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_REPORT: saved dialog screenshot to {reportScreenshotPath}");
                }

                if (Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_REPORT_SAVE") == "1")
                {
                    // Not SaveAsFileAction: that goes through ViewDomain.OpenSaveFileDialog ->
                    // IStorageProvider.SaveFilePickerAsync, which never completes in headless mode
                    // (confirmed the hard way: hung indefinitely, same class of issue as the
                    // simulated-input problems found earlier in Phase 5) - there's no real file
                    // picker UI for it to resolve against. ReportEngine.ExecuteReport is the
                    // actual export logic underneath that dialog; calling it directly against a
                    // real MemoryStream verifies TaskListsReportProcessor genuinely produces
                    // report bytes without depending on headless file-picker support.
                    //
                    // CSV, not Excel: a first attempt at the Excel mime type got all the way into
                    // DataTableReportProcessor.FormatDataTableToExcelWorksheet before throwing
                    // DllNotFoundException for libgdiplus - EPPlus 4.5.3.3's AutoFitColumns() uses
                    // System.Drawing.Font for text measurement, which needs libgdiplus installed
                    // on macOS/Linux (not present on this dev Mac, confirmed via the real
                    // exception, not assumed) - a genuine cross-platform gap in Kapok.Report's
                    // Excel export path itself, out of scope to fix here (core repo). CSV export
                    // (ProcessToCsvStream) doesn't touch System.Drawing at all, so it proves the
                    // rest of the pipeline (ReportEngine, TaskListsReportProcessor,
                    // ProcessToDataTable) end to end without depending on a system library this
                    // Mac doesn't have.
                    // Guarded like the OpenReportDialog call above: ReportEngine.GetOrCreateReportModel
                    // (Kapok.Report.BusinessLayer.ReportModelService.GetOrCreateFromType) queries then
                    // inserts a ReportModel row keyed by TypeFullName with no locking between the
                    // independent IDataDomainScope each ReportEngine public method opens for itself -
                    // MimeTypeReportPage's constructor alone calls three such methods back to back
                    // (GetOrCreateReportLayout, GetSupportedMimeTypes, and this ExecuteReport call).
                    // First seen failing here on Linux CI (SQLite Error 19: UNIQUE constraint failed:
                    // ReportModel.TypeFullName) though dozens of local macOS runs never hit it - a
                    // genuine race in the external Kapok.Report package, out of scope to fix in this
                    // repo. Catching it here keeps this diagnostic scenario from taking the whole
                    // process down (exit 134) over an unrelated package's bug.
                    try
                    {
                        using var stream = new MemoryStream();
                        new global::Kapok.Report.ReportEngine(GetService<IDataDomain>()).ExecuteReport(
                            new ToDoAvaloniaApp.Report.TaskListsReport(),
                            new Dictionary<string, object>
                            {
                                [nameof(ToDoAvaloniaApp.Report.TaskListsReport.IncludeArchived)] = true,
                                // Real values for the two new parameters (Phase 8 item 7), not just
                                // structurally present - proves TaskListsReportProcessor genuinely
                                // reads them back out of ParameterValues by name.
                                [nameof(ToDoAvaloniaApp.Report.TaskListsReport.SortBy)] = "IsArchived",
                                [nameof(ToDoAvaloniaApp.Report.TaskListsReport.GeneratedOn)] = new DateTime(2026, 1, 15)
                            },
                            "text/csv",
                            stream);
                        Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_REPORT: ExecuteReport produced {stream.Length} bytes:");
                        Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_REPORT: ExecuteReport threw: {ex}");
                    }
                }

                reportWindow?.Close();

                // Program.cs's own end-of-run capture tracks the last-opened window separately
                // and would try to screenshot this one right after it's closed/disposed above -
                // this verification path already saved everything it needs (the dialog
                // screenshot and the exported bytes), so it exits cleanly here instead of letting
                // control return to a capture that can only fail on a disposed window.
                Console.WriteLine("KAPOK_HEADLESS_SCREENSHOT_REPORT: done");
                Environment.Exit(0);
            });
            Dispatcher.UIThread.RunJobs();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShutdownApplication(int exitCode)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown(exitCode);
    }
}
