using Avalonia.Controls;
using Avalonia.Data;
using Kapok.View.Avalonia.DefaultPageControls;

namespace Kapok.View.Avalonia.Windows;

/// <summary>
/// Generic dialog host window. Matches Kapok.View.Wpf's DialogPageWindow.xaml.
/// </summary>
public class DialogPageWindow : Window
{
    public DialogPageWindow()
    {
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        var content = new ContentControl { ContentTemplate = new PageContentTemplate() };
        content.Bind(ContentControl.ContentProperty, new Binding());

        Content = content;
        this.Bind(TitleProperty, new Binding(nameof(IPage.Title)));
    }
}
