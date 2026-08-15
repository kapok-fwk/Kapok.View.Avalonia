using Avalonia.Controls;

namespace Kapok.View.Avalonia.DefaultPageControls;

/// <summary>
/// Generic ICardPage control. Matches Kapok.View.Wpf's CardPageControl.xaml, which - as of this
/// port - is itself not yet implemented ("A generic page control solution for pages implementing
/// ICardPage is not yet implemented"); real card pages are expected to supply their own control
/// via ViewDomain.RegisterPageWpfControlType-equivalent registration. Kept as a real (if minimal)
/// fallback rather than throwing, so a page falling through to this doesn't crash the app.
/// </summary>
public class CardPageView : UserControl
{
    public CardPageView()
    {
        Content = new MenuToolbar();
    }
}
