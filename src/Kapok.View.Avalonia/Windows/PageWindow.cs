using Avalonia.Controls;
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

        var content = new ContentControl { ContentTemplate = new PageContentTemplate() };
        content.Bind(ContentControl.ContentProperty, new Binding());

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

        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                new Grid { Height = 33, [DockPanel.DockProperty] = Dock.Bottom, Children = { okButton } },
                content
            }
        };

        this.Bind(TitleProperty, new Binding(nameof(IPage.Title)));
        this.Bind(InteractiveMenu.SelectedItemsBindingProperty, new Binding("DataSet.SelectedEntries"));

        DataContextChanged += (_, _) => OnDataContextSet();
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

    private void RebuildRibbon()
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
