using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Markup.Xaml.Styling;
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
                    newTaskList.Name = "Groceries";

                if (page is Tasks && dataSet.Current is DataModel.Task newTask)
                {
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
                    using var stream = new MemoryStream();
                    new global::Kapok.Report.ReportEngine(GetService<IDataDomain>()).ExecuteReport(
                        new ToDoAvaloniaApp.Report.TaskListsReport(),
                        new Dictionary<string, object> { [nameof(ToDoAvaloniaApp.Report.TaskListsReport.IncludeArchived)] = true },
                        "text/csv",
                        stream);
                    Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT_REPORT: ExecuteReport produced {stream.Length} bytes:");
                    Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
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
