using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Kapok.View.Avalonia.Windows;

namespace Kapok.View.Avalonia.Dock;

/// <summary>
/// Dock-flavored AvaloniaViewDomain, mirroring Kapok.View.Wpf.AvalonDock's WpfAvalonDockViewDomain:
/// swaps the default card/list page window for a Dock-hosting variant (DockPageWindow) and adds
/// FocusDocumentPage, walking Dock.Avalonia's own layout tree instead of AvalonDock's
/// Layout.Descendents().
/// </summary>
public class AvaloniaDockViewDomain : AvaloniaViewDomain
{
    public AvaloniaDockViewDomain(Action<int> shutdownApplicationAction, IServiceProvider? serviceProvider = null)
        : base(shutdownApplicationAction, serviceProvider)
    {
        DefaultCardPageWindow = typeof(DockPageWindow);
        DefaultListPageWindow = typeof(DockPageWindow);
        DefaultPopupListPageWindow = typeof(PopupListPageWindow);
    }

    /// <summary>
    /// Not yet exercised by ToDoAvaloniaApp (its pages don't have a DocumentPageCollectionPage
    /// host page - see MainPage.cs's Phase 1 simplification note), but built to mirror
    /// WpfAvalonDockViewDomain.FocusDocumentPage's contract for when Phase 3's follow-up work
    /// gives DocumentPageCollectionPage a real Avalonia host.
    /// </summary>
    public virtual void FocusDocumentPage(DocumentPageCollectionPage hostPage, IPage documentPage)
    {
        var window = GetOwnerWindow(hostPage);
        if (window is not DockPageWindow dockWindow)
            throw new NotSupportedException("The owner window is not a DockPageWindow.");

        var documentDockable = FindDockableByContext(dockWindow.Factory.GetDockable<IDock>("Documents"), documentPage);
        if (documentDockable != null)
            dockWindow.Factory.SetActiveDockable(documentDockable);
    }

    private static IDockable? FindDockableByContext(IDock? dock, object? context)
    {
        if (dock?.VisibleDockables == null)
            return null;

        foreach (var dockable in dock.VisibleDockables)
        {
            if (ReferenceEquals(dockable.Context, context))
                return dockable;
            if (dockable is IDock nested)
            {
                var found = FindDockableByContext(nested, context);
                if (found != null)
                    return found;
            }
        }

        return null;
    }
}
