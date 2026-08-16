using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Core;
using Kapok.View.Avalonia.DefaultPageControls;

namespace Kapok.View.Avalonia.Dock;

/// <summary>
/// Resolves a Dock.Avalonia dockable (Document/Tool) whose Context is an IPage to that page's
/// hosting Control, delegating to Kapok.View.Avalonia's PageContentTemplate for the actual
/// IPage-&gt;Control resolution.
///
/// Needed because Dock.Avalonia's DocumentContentControl/ToolContentControl present the dockable
/// itself as Content, not dockable.Context directly (confirmed empirically: a plain
/// PageContentTemplate, which only matches IPage, never matched anything - the ContentPresenter's
/// data was the IDockable, not the IPage inside it. DockControl.AutoCreateDataTemplates's own doc
/// comment confirms this split: "adds default DataTemplates for all dock types" [not their
/// Context] - the intended pattern per Dock's own samples is a template matching the dockable
/// that internally rebinds to .Context, which is exactly what this class does).
/// </summary>
public class DockableContentTemplate : IDataTemplate
{
    public bool Match(object? data) => data is IDockable { Context: IPage };

    public Control? Build(object? param)
    {
        if (param is not IDockable { Context: IPage page })
            return null;

        return new PageContentTemplate().Build(page);
    }
}
