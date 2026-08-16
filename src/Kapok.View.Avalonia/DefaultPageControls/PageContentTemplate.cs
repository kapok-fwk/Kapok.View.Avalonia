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

        // Kapok.View.Wpf wires these through PageControlStyling.xaml's Style (an
        // EventTrigger-per-UserControl workaround for WPF's inability to put multiple Behaviors
        // directly on a Style Setter - see InteractivityHelper.cs, confirmed in Phase 2 to be used
        // only by this file and DataGridStyling.xaml). Avalonia has no such restriction, so this
        // is just two plain event subscriptions - but the actual effect matters far more than the
        // XAML mechanism: IPage.OnLoadingAction/OnLoadedAction (Page.cs) were never invoked
        // anywhere in this port before this, meaning ListPage<TEntry>.OnLoaded()'s DataSet.Load()
        // call - the thing that actually populates a list page from the repository when it's
        // first shown - has never run either. Went unnoticed through Phases 1-4 because
        // ToDoAvaloniaApp always starts from an empty in-memory database, so "did Load() run"
        // was never observable: rows only ever appeared via CreateNewEntryAction populating the
        // same live Collection directly, never via a real reload of pre-existing data.
        // OnLoadingAction/OnLoadedAction are declared on the concrete Page base class, not on
        // IPage itself - every real page in this port derives from Page (InteractivePage/
        // DataPage/ListPage/CardPage all do), so this cast is safe in practice, matching the
        // same is-Page pattern AvaloniaViewDomain.Window_Closing/Window_Closed already use.
        if (page is Page pageObject)
        {
            control.Initialized += (_, _) => pageObject.OnLoadingAction.Execute();
            control.Loaded += (_, _) => pageObject.OnLoadedAction.Execute();
        }

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
