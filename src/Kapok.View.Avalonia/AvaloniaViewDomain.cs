using System.ComponentModel;
using System.Runtime.CompilerServices;
using global::Avalonia.Controls;
using global::Avalonia.Threading;
using Kapok.BusinessLayer;
using Kapok.Data;
using Kapok.Entity.Model;
using Kapok.Report.DataModel;
using Kapok.View.Avalonia.Data;
using Kapok.View.Avalonia.DefaultPageControls;
using Kapok.View.Avalonia.Windows;

namespace Kapok.View.Avalonia;

public interface IAvaloniaViewDomain
{
    /// <summary>
    /// The default page window for a page inheriting <see cref="IDialogPage"/>.
    /// </summary>
    Type DefaultDialogPageWindow { get; set; }

    /// <summary>
    /// The default page window for a page inheriting <see cref="ICardPage"/>.
    /// </summary>
    Type DefaultCardPageWindow { get; set; }

    /// <summary>
    /// The default page window for a page inheriting <see cref="IListPage"/>.
    /// </summary>
    Type DefaultListPageWindow { get; set; }

    /// <summary>
    /// The default page window for a page inheriting <see cref="IListPage"/> displaying a
    /// less heavy menu than the default page window.
    /// </summary>
    Type DefaultPopupListPageWindow { get; set; }
}

/// <summary>
/// The Avalonia implementation of <see cref="ViewDomain"/>.
///
/// Mirrors Kapok.View.Wpf's WpfViewDomain member-for-member (same abstract contract), swapping
/// WPF's Window/MessageBox/Microsoft.Win32 dialogs for Avalonia's Window/StorageProvider
/// equivalents. See Kapok.View.Avalonia's porting plan for what's intentionally deferred to a
/// later phase (marked below with NotSupportedException).
/// </summary>
public class AvaloniaViewDomain : ViewDomain, IAvaloniaViewDomain
{
    private static readonly Dictionary<Type, Func<Window>> PageWindowConstructors = new();
    private static readonly Dictionary<Type, Type> PageControlTypes = new();
    private readonly Dictionary<IPage, IEnumerable<IPage>> _pageContainer = new(); // key = owning page, value = collection with pages the page has in its container. A page can only have one container
    protected static readonly Dictionary<IPage, Window> PageWindows = new();

    /// <summary>
    /// A internal dictionary with weak relationship holding the Avalonia Control classes
    /// for each active page in the UI.
    /// </summary>
    private readonly ConditionalWeakTable<IPage, Control> _pageContentControl = new();

    public static void RegisterPageWindowConstructor<TPage>(Func<Window> constructWindow)
        where TPage : class, IPage
    {
        PageWindowConstructors[typeof(TPage)] = constructWindow;
    }

    public static void RegisterPageControlType<TPage>(Type controlType)
        where TPage : class, IPage
    {
        PageControlTypes[typeof(TPage)] = controlType;
    }

    public AvaloniaViewDomain(Action<int> shutdownApplicationAction, IServiceProvider? serviceProvider = null)
        : base(serviceProvider)
    {
        ShutdownApplication = shutdownApplicationAction;
    }

    #region Configuration

    private static void TestTypeParameterlessPublicConstructor(Type type)
    {
        if (!type.IsClass)
            throw new NotSupportedException();

        var constructorInfo = type.GetConstructor(Type.EmptyTypes);
        if (constructorInfo == null)
            throw new NotSupportedException("The type must have an public parameterless constructor");
    }

    private Type _defaultDialogPageWindow = typeof(DialogPageWindow);
    private Type _defaultCardPageWindow = typeof(PageWindow);
    private Type _defaultListPageWindow = typeof(PageWindow);
    private Type _defaultPopupListPageWindow = typeof(PageWindow);

    public Type DefaultDialogPageWindow
    {
        get => _defaultDialogPageWindow;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!typeof(Window).IsAssignableFrom(value))
                throw new NotSupportedException($"The default dialog page type must be assignable from type {typeof(Window).FullName}.");

            TestTypeParameterlessPublicConstructor(value);
            _defaultDialogPageWindow = value;
        }
    }

    public Type DefaultCardPageWindow
    {
        get => _defaultCardPageWindow;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!typeof(Window).IsAssignableFrom(value))
                throw new NotSupportedException($"The default card page type must be assignable from type {typeof(Window).FullName}.");

            TestTypeParameterlessPublicConstructor(value);
            _defaultCardPageWindow = value;
        }
    }

    public Type DefaultListPageWindow
    {
        get => _defaultListPageWindow;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!typeof(Window).IsAssignableFrom(value))
                throw new NotSupportedException($"The default list page type must be assignable from type {typeof(Window).FullName}.");

            TestTypeParameterlessPublicConstructor(value);
            _defaultListPageWindow = value;
        }
    }

    public Type DefaultPopupListPageWindow
    {
        get => _defaultPopupListPageWindow;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!typeof(Window).IsAssignableFrom(value))
                throw new NotSupportedException($"The default popup list page type must be assignable from type {typeof(Window).FullName}.");

            TestTypeParameterlessPublicConstructor(value);
            _defaultPopupListPageWindow = value;
        }
    }

    #endregion

    public override Type GetPageControlType(Type pageType)
    {
        if (PageControlTypes.TryGetValue(pageType, out var type))
            return type;

        if (typeof(IListPage).IsAssignableFrom(pageType))
            return typeof(ListPageView);
        if (typeof(ICardPage).IsAssignableFrom(pageType))
            return typeof(CardPageView);
        return typeof(BlankPageView);
    }

    private Window ConstructWindow(Type pageType)
    {
        if (PageWindowConstructors.TryGetValue(pageType, out var constructor))
            return constructor.Invoke();

        if (typeof(QuestionDialogPage).IsAssignableFrom(pageType))
            return new QuestionDialogPageWindow();

        if (typeof(IListPage).IsAssignableFrom(pageType))
            return (Window)Activator.CreateInstance(DefaultListPageWindow)!;
        if (typeof(IDialogPage).IsAssignableFrom(pageType))
            return (Window)Activator.CreateInstance(DefaultDialogPageWindow)!;
        if (typeof(ICardPage).IsAssignableFrom(pageType))
            return (Window)Activator.CreateInstance(DefaultCardPageWindow)!;

        throw new NotSupportedException($"No Avalonia window defined for page {pageType.FullName}");
    }

    private void CheckWindowAlreadyOpen(IPage page)
    {
        if (PageWindows.ContainsKey(page) || _pageContentControl.TryGetValue(page, out _))
            throw new NotSupportedException("The page is already opened.");
        if (_pageContainer.Values.FirstOrDefault(pc => pc.Contains(page)) != null)
            throw new NotSupportedException("The page is already opened in a page container.");
    }

    private void ConstructPageWindow(IPage page)
    {
        var newWindow = ConstructWindow(page.GetType());
        newWindow.DataContext = page;

        newWindow.Closing += Window_Closing;
        newWindow.Closed += Window_Closed;

        PageWindows.Add(page, newWindow);
    }

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (sender is not Window window) return;

        var page = PageWindows.FirstOrDefault(pair => pair.Value == window).Key;
        if (page is Page pageObject)
        {
            var cancelEventArgs = new CancelEventArgs();
            pageObject.RaiseClosing(cancelEventArgs);
            if (cancelEventArgs.Cancel)
                e.Cancel = true;
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;

        var page = PageWindows.FirstOrDefault(pair => pair.Value == window).Key;
        if (page != null)
        {
            if (page is Page pageObject)
                pageObject.RaiseClosed();

            PageWindows.Remove(page);
        }

        window.Closing -= Window_Closing;
        window.Closed -= Window_Closed;
    }

    public override void ShowPage(IPage page)
    {
        CheckWindowAlreadyOpen(page);
        ConstructPageWindow(page);
        PageWindows[page].Show();
    }

    protected Window GetOwnerWindow(IPage ownerPage)
    {
        var owningPageContainer = (
            from p in _pageContainer
            where p.Value.Contains(ownerPage)
            select p.Key
        ).FirstOrDefault();
        if (owningPageContainer != null)
        {
            // NOTE: no recursion check here to prevent endless loops, matching WpfViewDomain
            return GetOwnerWindow(owningPageContainer);
        }

        if (!PageWindows.ContainsKey(ownerPage))
            throw new ArgumentException("The owner page does not have an active, open window.", nameof(ownerPage));

        return PageWindows[ownerPage];
    }

    public override bool? ShowDialogPage(IPage page, IPage? ownerPage = null)
    {
        CheckWindowAlreadyOpen(page);
        ConstructPageWindow(page);

        var window = PageWindows[page];
        Window? owner = ownerPage != null ? GetOwnerWindow(ownerPage) : null;

        // Avalonia's ShowDialog is async-only; ViewDomain's contract is synchronous (matching
        // WPF's Window.ShowDialog()), so this pumps the dispatcher until the dialog closes -
        // the same "block the calling thread but keep the UI responsive" behavior WPF gives you
        // for free. Only safe because this is always called from the UI thread.
        bool? result = null;
        var frame = new DispatcherFrame();

        async void RunDialog()
        {
            try
            {
                result = owner != null
                    ? await window.ShowDialog<bool?>(owner)
                    : await ShowDialogWithoutOwner(window);
            }
            finally
            {
                frame.Continue = false;
            }
        }

        RunDialog();
        Dispatcher.UIThread.PushFrame(frame);

        return result;
    }

    private static async Task<bool?> ShowDialogWithoutOwner(Window window)
    {
        // Avalonia requires an owner window for ShowDialog(); fall back to whatever the
        // application's main/active window is.
        var owner = (global::Avalonia.Application.Current?.ApplicationLifetime as
            global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (owner != null && owner != window)
            return await window.ShowDialog<bool?>(owner);

        window.Show();
        var tcs = new TaskCompletionSource<bool?>();
        window.Closed += (_, _) => tcs.TrySetResult(null);
        return await tcs.Task;
    }

    public override void RegisterPageContainer(IPage owningPage, IEnumerable<IPage> pageContainer)
    {
        if (_pageContainer.ContainsKey(owningPage))
            throw new ArgumentException("There is already a page container registered for this page", nameof(owningPage));
        if (_pageContainer.ContainsValue(pageContainer))
            throw new ArgumentException("The page container is already for another page", nameof(pageContainer));

        _pageContainer.Add(owningPage, pageContainer);
    }

    public override void UnregisterPageContainer(IPage owningPage)
    {
        _pageContainer.Remove(owningPage);
    }

    public override void ClosePage(IPage page)
    {
        _pageContainer.Remove(page);

        var pageInPageContainer = _pageContainer.Values.FirstOrDefault(pc => pc.Contains(page));
        if (pageInPageContainer != null)
        {
            if (page is Page pageObject)
                pageObject.RaiseClosed();

            if (pageInPageContainer is ICollection<IPage> collection)
                collection.Remove(page);

            return;
        }

        if (!PageWindows.ContainsKey(page))
            return; // optimistic behavior, matching WpfViewDomain

        PageWindows[page].Close();
    }

    public override IQueryableView<TEntity> CreateQueryableView<TEntity>(IQueryable<TEntity> queryable)
    {
        return new AvaloniaQueryableView<TEntity> { QueryableSource = queryable };
    }

    public override IPropertyLookupView CreatePropertyLookupView(ILookupDefinition lookupDefinition, IDataDomain dataDomain, Func<object?>? currentSelector = null)
    {
        return new AvaloniaPropertyLookupView(lookupDefinition, dataDomain, currentSelector);
    }

    public override IDataSetView<TEntry> CreateDataSetView<TEntry>(IDataDomainScope dataDomainScope, IEntityService<TEntry>? entityService = null)
    {
        // The core DataSetView<TEntry> (Kapok.View) already exposes a plain ObservableCollection
        // via Collection/IDataSetReadonlyView<TEntry>.Collection, and its sort/filter/add/delete
        // pipeline is UI-framework-agnostic (server-side re-query, not a client-side
        // ICollectionView). WPF's WpfDataSetView<TEntry> only adds WPF DataGrid-specific sugar on
        // top (CollectionViewSource/IEditableCollectionView for the {NewItemPlaceholder} inline
        // add-row and grid-native sort/group). None of that is needed for Phase 1's plain-window
        // list binding, so we bind directly to the core class - an Avalonia-specific subclass can
        // be introduced in the DataGrid phase if the eventual grid choice needs similar hooks.
        return new DataSetView<TEntry>(ServiceProvider, dataDomainScope, entityService);
    }

    public override IHierarchyDataSetView<TEntry> CreateHierarchyDataSetView<TEntry>(IDataDomainScope dataDomainScope, IEntityService<TEntry>? entityService = null)
    {
        // WPF's HierarchyDataSetView<TEntry> (600+ lines: expand/collapse, move-in/move-out tree
        // navigation) is built entirely on top of WPF's ICollectionView.CurrentItem/
        // MoveCurrentToPrevious/Next - there is no core, framework-agnostic equivalent to build on
        // top of the way there was for the flat case. A real Avalonia port is its own scoped
        // workstream, not part of this phase (ToDoAvaloniaApp has no hierarchical data either).
        throw new NotSupportedException(
            "Hierarchical data sets are not yet supported by Kapok.View.Avalonia. " +
            "This is a known gap, tracked as follow-up work beyond the current phased build.");
    }

    public override void ShowInfoMessage(string message, string? title = null, IPage? ownerPage = null)
        => MessageBoxWindow.Show(message, title ?? "Information", MessageBoxWindow.MessageBoxKind.Info, ownerPage != null ? GetOwnerWindow(ownerPage) : null);

    public override void ShowErrorMessage(string message, string? title = null, IPage? ownerPage = null, Exception? exception = null)
    {
        if (exception != null)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine(message);
            text.AppendLine(exception.Message);
            var innerException = exception.InnerException;
            while (innerException != null)
            {
                text.AppendLine("--");
                text.AppendLine(innerException.Message);
                innerException = innerException.InnerException;
            }
            message = text.ToString();
        }

        MessageBoxWindow.Show(message, title ?? "Error", MessageBoxWindow.MessageBoxKind.Error, ownerPage != null ? GetOwnerWindow(ownerPage) : null);
    }

    public override bool ShowYesNoQuestionMessage(string message, string? title = null, IPage? ownerPage = null)
        => MessageBoxWindow.ShowYesNo(message, title ?? "Question", ownerPage != null ? GetOwnerWindow(ownerPage) : null);

    public override bool ShowConfirmMessage(string message, string? title = null, IPage? ownerPage = null)
        => MessageBoxWindow.ShowOkCancel(message, title ?? "Confirm", ownerPage != null ? GetOwnerWindow(ownerPage) : null);

    internal void RegisterPageContentControl(IPage page, Control control)
    {
        _pageContentControl.AddOrUpdate(page, control);
    }

    internal void RemovePageContentControl(IPage page)
    {
        _pageContentControl.Remove(page);
    }

    private Control? GetPageContentControl(IPage? page)
    {
        if (page == null) return null;
        return _pageContentControl.TryGetValue(page, out var control) ? control : null;
    }

    public override void PageEndEdit(IPage page)
    {
        var pageContentControl = GetPageContentControl(page);
        if (pageContentControl == null)
            return; // optimistic behavior

        // Force whatever text box / editor currently has focus to push its pending edit into the
        // bound view model, mirroring WpfViewDomain.PageEndEdit's FocusManager walk. Avalonia
        // doesn't have WPF's explicit DependencyObject.EndEdit(); moving focus to the page's own
        // content control causes the previously-focused editor to lose focus, which flushes any
        // pending TwoWay binding using the default LostFocus update trigger.
        pageContentControl.Focus();
    }

    public override void StartEditingDefaultDataGridCurrentEntity(IDataPage page, bool enforceFirstEditableRow)
    {
        // No real data grid exists yet (see the DataGrid phase) - safe no-op for now rather than
        // a hard failure, since ListPage<TEntry>.CreateNewEntry()/EditEntry() call this as part of
        // their normal flow and shouldn't throw just because inline grid-cell editing isn't wired
        // up yet.
    }

    public override string? OpenOpenFileDialog(string title, string fileMask, IPage? ownerPage = null)
    {
        var topLevel = GetTopLevelForDialog(ownerPage);
        var task = topLevel.StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = FileDialogFilters.Parse(fileMask)
        });

        var result = RunOnUIThreadSynchronously(task);
        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    public override string? OpenSaveFileDialog(string title, string fileMask, IPage? ownerPage = null)
    {
        var topLevel = GetTopLevelForDialog(ownerPage);
        var task = topLevel.StorageProvider.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = title,
            FileTypeChoices = FileDialogFilters.Parse(fileMask)
        });

        var result = RunOnUIThreadSynchronously(task);
        return result?.Path.LocalPath;
    }

    private TopLevel GetTopLevelForDialog(IPage? ownerPage)
    {
        var window = ownerPage != null ? GetOwnerWindow(ownerPage) : null;
        window ??= (global::Avalonia.Application.Current?.ApplicationLifetime as
            global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (window == null)
            throw new NotSupportedException("No window is available to own the file dialog.");

        return window;
    }

    private static TResult RunOnUIThreadSynchronously<TResult>(Task<TResult> task)
    {
        // Same dispatcher-pumping approach as ShowDialogPage - StorageProvider's API is
        // Task-based only, but ViewDomain's contract is synchronous.
        var frame = new DispatcherFrame();
        TResult? result = default;
        Exception? error = null;

        task.ContinueWith(t =>
        {
            if (t.IsFaulted) error = t.Exception;
            else result = t.Result;
            frame.Continue = false;
        }, TaskScheduler.FromCurrentSynchronizationContext());

        Dispatcher.UIThread.PushFrame(frame);

        if (error != null) throw error;
        return result!;
    }

    public override bool OpenReportDialog(object model, object? reportLayout = null, IPage? ownerPage = null)
    {
        if (model is not Kapok.Report.Model.Report reportModel)
            throw new ArgumentException(
                $"The parameter {nameof(model)} must be assignable to the type {typeof(Kapok.Report.Model.Report).FullName}.");

        var page = new Report.MimeTypeReportPage(
            reportModel,
            ServiceProvider,
            reportLayout as Kapok.Report.DataModel.ReportLayout);

        var result = ownerPage == null ? page.ShowDialog() : page.ShowDialog(ownerPage);
        return result ?? false;
    }

    public override void OpenFile(string filename)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = filename
        };
        System.Diagnostics.Process.Start(psi);
    }

    public override void BusinessLayerMessageEventToSingleUIMessage(object? businessLayerObject, ReportBusinessLayerMessageEventArgs eventArgs)
    {
        switch (eventArgs.Message.Severity)
        {
            case MessageSeverity.Info:
                ShowInfoMessage(eventArgs.Message.Text);
                break;
            case MessageSeverity.Warning:
                ShowInfoMessage(eventArgs.Message.Text, "Warning");
                break;
            case MessageSeverity.Error:
                ShowErrorMessage(eventArgs.Message.Text);
                break;
        }
    }
}
