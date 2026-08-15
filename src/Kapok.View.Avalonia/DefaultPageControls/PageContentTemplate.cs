using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Kapok.View.Avalonia.DefaultPageControls;

/// <summary>
/// Resolves an IPage to its hosting Control via AvaloniaViewDomain.GetPageControlType and
/// instantiates it, registering/unregistering it with AvaloniaViewDomain's page-content-control
/// table (needed for PageEndEdit/StartEditingDefaultDataGridCurrentEntity) as it enters/leaves the
/// visual tree.
///
/// Mirrors Kapok.View.Wpf's TemplateGenerator.cs + PageTemplateSelector.cs, but is much simpler:
/// Avalonia's IDataTemplate builds a visual tree from a delegate natively, so the
/// FrameworkElementFactory-based workaround those two WPF classes existed for isn't needed here.
/// </summary>
public class PageContentTemplate : IDataTemplate
{
    public bool Match(object? data) => data is IPage;

    public Control? Build(object? param)
    {
        if (param is not IPage page)
            return null;

        var controlType = page.ViewDomain.GetPageControlType(page.GetType());
        var control = (Control)Activator.CreateInstance(controlType)!;
        control.DataContext = page;

        control.AttachedToVisualTree += (_, _) =>
        {
            if (page.ViewDomain is AvaloniaViewDomain avaloniaViewDomain)
                avaloniaViewDomain.RegisterPageContentControl(page, control);
        };
        control.DetachedFromVisualTree += (_, _) =>
        {
            if (page.ViewDomain is AvaloniaViewDomain avaloniaViewDomain)
                avaloniaViewDomain.RemovePageContentControl(page);
        };

        return control;
    }
}
