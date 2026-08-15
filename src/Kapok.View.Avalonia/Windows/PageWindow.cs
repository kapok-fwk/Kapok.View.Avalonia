using Avalonia.Controls;
using Avalonia.Data;
using Kapok.View.Avalonia.DefaultPageControls;

namespace Kapok.View.Avalonia.Windows;

/// <summary>
/// The generic (non-Ribbon, non-Dock) page window - serves as the default for card and list
/// pages in this phase. Matches the shape of Kapok.View.Wpf's CardPageWindow/ListPageWindow
/// (toolbar/ribbon + hosted page content), minus the Ribbon chrome itself, which is a later
/// phase (see the porting plan's Phase 2).
/// </summary>
public class PageWindow : Window
{
    public PageWindow()
    {
        Width = 900;
        Height = 650;

        var content = new ContentControl { ContentTemplate = new PageContentTemplate() };
        content.Bind(ContentControl.ContentProperty, new Binding());

        Content = content;
        this.Bind(TitleProperty, new Binding(nameof(IPage.Title)));
    }
}
