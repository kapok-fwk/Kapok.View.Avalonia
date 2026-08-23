using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using AvaloniaUI.Ribbon.Desktop;
using Kapok.View.Avalonia.DefaultPageControls;
using Kapok.View.Avalonia.ValueConverter;

namespace Kapok.View.Avalonia.Windows;

/// <summary>
/// The default page window for card and list pages - matches the shape of Kapok.View.Wpf's
/// CardPageWindow.xaml/ListPageWindow.xaml (RibbonWindow + Ribbon + hosted page content + OK
/// button). One class covers both roles, same as Phase 1's plain-toolbar version did - WPF's two
/// XAML files differ only in which subset of DataPage/ListPage KeyBindings they declare, and
/// binding to an action a card page doesn't have (e.g. EditEntryAction) just never resolves,
/// the same as it would in WPF.
///
/// Phase 2: now a real RibbonWindow with a real Ribbon built from the page's Base menu (see
/// RibbonMenuBuilder), replacing Phase 1's flat MenuToolbar fallback.
/// </summary>
public class PageWindow : RibbonWindow
{
    public PageWindow()
    {
        Width = 900;
        Height = 650;

        // AvaloniaControls.Ribbon.Desktop ships its control templates/themes as a separate style
        // resource, not auto-registered by referencing the package (mirrors how
        // Avalonia.Themes.Fluent needs Styles.Add(new FluentTheme()) at the app level) - added
        // per-window rather than requiring every consuming app's App.axaml/App.cs to know about
        // it, keeping Kapok.View.Avalonia self-contained the same way Phase 1's converters/
        // templates already are. The Desktop package's aggregator style already merges in the
        // base AvaloniaControls.Ribbon package's control styles (RibbonTab/RibbonButton/etc.), so
        // only this one include is needed.
        Styles.Add(new StyleInclude(new Uri("avares://Kapok.View.Avalonia/"))
        {
            Source = new Uri("avares://AvaloniaUI.Ribbon.Desktop/Styles/Fluent/AvaloniaRibbon.axaml")
        });

        var okButton = new Button
        {
            Content = "OK",
            Width = 75,
            Height = 23,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new global::Avalonia.Thickness(0, 5, 10, 10)
        };
        okButton.Bind(Button.CommandProperty, new Binding($"{nameof(IPage.CloseAction)}") { Converter = new IActionToICommandConverter() });

        // AvaloniaControls.Ribbon.Desktop's RibbonWindow supplies its own Window template
        // (that's the whole point of it - Ribbon chrome instead of a plain title bar), which
        // doesn't include the VisualLayerManager/OverlayLayer a stock Avalonia Window's default
        // template provides. Popups (confirmed via a real headless screenshot: opening a
        // LookupComboBox's dropdown inside a DockPageWindow threw
        // "Unable to create IPopupImpl and no overlay layer is found for the target control")
        // need one somewhere in the visual tree to render into when overlay-mode popups are used
        // (the default in Avalonia.Headless, and generally preferred over real separate popup
        // windows) - so this wraps the window's whole content in one explicitly, rather than
        // leaving every popup-using control (LookupComboBox now, ContextMenu/Flyout/ToolTip
        // later) to silently fail only inside Ribbon-hosted windows.
        Content = new VisualLayerManager
        {
            // EnableOverlayLayer defaults to false (unlike EnableAdornerLayer) - confirmed by
            // reading VisualLayerManager.cs - so it has to be turned on explicitly, or
            // OverlayLayer.GetOverlayLayer keeps returning null even once a VisualLayerManager
            // is actually present in the tree, and popups keep throwing.
            EnableOverlayLayer = true,
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    new Grid { Height = 33, [DockPanel.DockProperty] = Dock.Bottom, Children = { okButton } },
                    BuildPageContentArea()
                }
            }
        };

        this.Bind(TitleProperty, new Binding(nameof(IPage.Title)));
        this.Bind(InteractiveMenu.SelectedItemsBindingProperty, new Binding("DataSet.SelectedEntries"));

        DataContextChanged += (_, _) => OnDataContextSet();
    }

    /// <summary>
    /// Builds the main content area between the Ribbon and the OK button. Overridden by
    /// Kapok.View.Avalonia.Dock's DockPageWindow to host a Dock.Avalonia DockControl instead of
    /// a plain ContentControl - kept virtual specifically so that subclass can reuse all of this
    /// class's Ribbon/KeyBindings/OK-button wiring rather than duplicating it.
    /// </summary>
    protected virtual Control BuildPageContentArea()
    {
        var content = new ContentControl { ContentTemplate = new PageContentTemplate() };
        content.Bind(ContentControl.ContentProperty, new Binding());
        return content;
    }

    private bool _keyBindingsAdded;

    private void OnDataContextSet()
    {
        RebuildRibbon();

        // Unlike Controls (which inherit DataContext reactively through the visual tree), KeyBinding
        // is a plain object - binding it before DataContext exists throws
        // "Cannot find a DataContext to bind to" instead of resolving lazily, so this has to wait
        // until DataContext is actually set (matches when ConstructPageWindow assigns it).
        if (!_keyBindingsAdded)
        {
            _keyBindingsAdded = true;
            AddDataPageKeyBindings();
        }
    }

    /// <summary>
    /// Protected (not private) so DocumentPageCollectionWindow can rebuild the Ribbon a second time
    /// after running OnLoadingAction - see that class's own comment on why: RibbonMenuBuilder
    /// builds statically from whatever Menu[Base].MenuItems holds at DataContextChanged time (its
    /// own doc comment already calls this out - "does not react to... after the initial build"),
    /// which for a DocumentPageCollectionPage host is before OnLoading() has run (OnLoadingAction
    /// only fires from Initialized, which happens later than DataContext being set), so a page like
    /// MainPage that replaces its own Base tab from OnLoading() (matching WPF's identical pattern)
    /// would otherwise show the pre-OnLoading tab forever.
    /// </summary>
    protected void RebuildRibbon()
    {
        if (DataContext is not InteractivePage page)
            return;

        var ribbon = new DesktopRibbon();
        RibbonMenuBuilder.Build(ribbon, page);
        Ribbon = ribbon;
    }

    /// <summary>
    /// Superset of Kapok.View.Wpf's CardPageWindow.xaml + ListPageWindow.xaml Window.InputBindings
    /// (the latter adds F2/Ctrl+Shift+F/Ctrl+E for ListPage-only actions). Bindings to actions a
    /// card page doesn't declare (e.g. EditEntryAction) simply never resolve at runtime, same as
    /// an unresolved WPF binding would - not worth two near-duplicate window classes to avoid.
    /// </summary>
    private void AddDataPageKeyBindings()
    {
        void Bind(string gesture, string actionPath)
        {
            KeyBindings.Add(new KeyBinding
            {
                Gesture = KeyGesture.Parse(gesture),
                [!KeyBinding.CommandProperty] = new Binding(actionPath) { Converter = new IActionToICommandConverter(), Source = DataContext }
            });
        }

        Bind("Escape", nameof(IPage.CloseAction));
        Bind("Ctrl+W", nameof(IPage.CloseAction));
        Bind("Ctrl+S", "SaveDataAction");
        Bind("F5", "RefreshAction");
        Bind("Ctrl+N", "CreateNewEntryAction");
        Bind("Ctrl+Delete", "DeleteEntryAction");
        Bind("F2", "EditEntryAction");
        Bind("Ctrl+Shift+F", "ToggleFilterVisibleAction");
        Bind("Ctrl+E", "ExportAsExcelSheetAction");
    }
}
