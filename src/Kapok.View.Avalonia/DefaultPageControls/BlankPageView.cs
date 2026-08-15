using Avalonia.Controls;

namespace Kapok.View.Avalonia.DefaultPageControls;

/// <summary>
/// Fallback control for any IPage that isn't an IListPage/ICardPage (e.g. plain InteractivePage
/// like ToDoAvaloniaApp's TestPage). Just shows the toolbar - matches
/// Kapok.View.Wpf's BlankDefaultPageControl (an empty placeholder).
/// </summary>
public class BlankPageView : UserControl
{
    public BlankPageView()
    {
        Content = new MenuToolbar();
    }
}
