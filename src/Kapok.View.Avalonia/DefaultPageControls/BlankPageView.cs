using Avalonia.Controls;
using Avalonia.Layout;

namespace Kapok.View.Avalonia.DefaultPageControls;

/// <summary>
/// Fallback control for any IPage that isn't an IListPage/ICardPage (e.g. plain InteractivePage
/// like ToDoAvaloniaApp's MainPage/TestPage). Direct port of Kapok.View.Wpf's
/// BlankDefaultPageControl - a placeholder message, not a toolbar. Phase 1 had this render a
/// MenuToolbar instead, since no Ribbon existed yet to show the page's Base menu; now that
/// PageWindow (see Windows/PageWindow.cs) has a real Ribbon covering that, duplicating it here
/// would just show the same actions twice.
/// </summary>
public class BlankPageView : UserControl
{
    public BlankPageView()
    {
        Content = new TextBlock
        {
            Text = "No default control is defined for page based on IPage.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap
        };
    }
}
