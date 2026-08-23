using Dock.Model.Core;

namespace Kapok.View.Avalonia.Dock;

/// <summary>
/// Port of Kapok.View.Wpf.AvalonDock's ActiveDocumentConverter - unwraps a docked item down to the
/// <see cref="IPage"/> it hosts, mirroring AvalonDock's LayoutDocument -&gt; IPage unwrap (WPF's
/// version handled both a raw IPage, already unwrapped, and a LayoutDocument wrapping one).
///
/// Only the unwrap direction is a stateless mapping like WPF's IValueConverter was. Going the other
/// way - given an IPage, which IDockable currently represents it - needs an actual lookup, because
/// Dock.Avalonia's dockables are not auto-created the way AvalonDock's DocumentsSource-bound
/// LayoutDocuments are (see DockPageWindow's own doc comment on this same gap for its one fixed
/// document). That lookup is necessarily stateful, so it lives with whoever is tracking the open
/// documents - <see cref="DocumentPageCollectionWindow"/> - rather than in this class.
/// </summary>
public static class ActiveDocumentConverter
{
    public static IPage? ToPage(object? dockableOrPage)
    {
        if (dockableOrPage is IPage page)
            return page;

        if (dockableOrPage is IDockable { Context: IPage dockablePage })
            return dockablePage;

        return null;
    }
}
