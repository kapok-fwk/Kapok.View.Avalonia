using Avalonia.Controls;
using Avalonia.Layout;

namespace Kapok.View.Avalonia.DefaultPageControls;

/// <summary>
/// Generic ICardPage control. Matches Kapok.View.Wpf's CardPageControl.xaml, which - as of this
/// port - is itself not yet implemented ("A generic page control solution for pages implementing
/// ICardPage is not yet implemented"); real card pages are expected to supply their own control
/// via ViewDomain.RegisterPageWpfControlType-equivalent registration. Kept as a real (if minimal)
/// fallback rather than throwing, so a page falling through to this doesn't crash the app.
/// Phase 2: dropped the MenuToolbar this used to show in Phase 1 - PageWindow's Ribbon (see
/// Windows/PageWindow.cs) now covers the page's Base menu, same reasoning as BlankPageView.
/// </summary>
public class CardPageView : UserControl
{
    public CardPageView()
    {
        Content = new TextBlock
        {
            Text = "A generic page control solution for pages implementing ICardPage is not yet implemented.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap
        };
    }
}
