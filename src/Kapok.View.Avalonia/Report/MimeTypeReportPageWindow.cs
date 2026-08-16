using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Kapok.View.Avalonia.ValueConverter;

namespace Kapok.View.Avalonia.Report;

/// <summary>
/// Avalonia equivalent of Kapok.View.Wpf's MimeTypeReportPageWindow.xaml - a plain (non-Ribbon)
/// dialog window, matching QuestionDialogPageWindow.cs's pattern (WPF's own report window is a
/// plain Window too, not RibbonWindow-hosted). Wires Initialized/Loaded -> OnLoadingAction/
/// OnLoadedAction directly (same as WPF's own XAML does with its own i:EventTrigger pair) since
/// this window doesn't go through PageContentTemplate (it's the window itself binding directly to
/// MimeTypeReportPage, like QuestionDialogPageWindow/MessageBoxWindow) - so it doesn't pick up the
/// PageContentTemplate fix from this phase's PageControlStyling item automatically.
/// </summary>
public class MimeTypeReportPageWindow : Window
{
    public MimeTypeReportPageWindow()
    {
        MinHeight = 130;
        MinWidth = 350;
        Width = 450;
        SizeToContent = SizeToContent.Height;

        this.Bind(TitleProperty, new Binding(nameof(IPage.Title)));

        var titleLabel = new TextBlock
        {
            FontSize = 16,
            Margin = new Thickness(8, 8, 8, 4)
        };
        titleLabel.Bind(TextBlock.TextProperty, new Binding(nameof(IPage.Title)));

        var parameterList = new ReportParameterList
        {
            MinHeight = 0,
            Margin = new Thickness(8, 0, 8, 8)
        };
        parameterList.Bind(ReportParameterList.ParameterListProperty, new Binding(nameof(MimeTypeReportPage.ReportParameters)));
        parameterList.Bind(Visual.IsVisibleProperty, new Binding(nameof(MimeTypeReportPage.ShowReportParameters)));

        var mimeTypeLabel = new TextBlock { Text = "File type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 8) };
        var mimeTypeCombo = new ComboBox
        {
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<MimeTypeReportPage.MimeTypeViewModel>((m, _) => new TextBlock { Text = m?.DisplayName })
        };
        mimeTypeCombo.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(MimeTypeReportPage.SupportedMimeTypes)));
        mimeTypeCombo.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(MimeTypeReportPage.SelectedMimeType)) { Mode = BindingMode.TwoWay });

        var mimeTypeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(mimeTypeLabel, 0);
        Grid.SetColumn(mimeTypeCombo, 1);
        mimeTypeRow.Children.Add(mimeTypeLabel);
        mimeTypeRow.Children.Add(mimeTypeCombo);

        Button MakeButton(string content) => new() { Content = content, MinWidth = 75, Margin = new Thickness(0, 8, 8, 8) };

        var designButton = MakeButton("Design");
        designButton.Bind(Button.CommandProperty, new Binding(nameof(MimeTypeReportPage.DesignAction)) { Converter = new IActionToICommandConverter() });
        designButton.Bind(Visual.IsVisibleProperty, new Binding(nameof(MimeTypeReportPage.IsDesignable)));

        // WPF's own window has this button too, always disabled ("a view of the data is not
        // developed yet" per its own TODO comment) - kept for layout/visual parity, not because
        // it does anything.
        var viewButton = MakeButton("View");
        viewButton.IsEnabled = false;

        var saveButton = MakeButton("Save as file");
        saveButton.Bind(Button.CommandProperty, new Binding(nameof(MimeTypeReportPage.SaveAsFileAction)) { Converter = new IActionToICommandConverter() });

        var cancelButton = MakeButton("Cancel");
        cancelButton.Bind(Button.CommandProperty, new Binding(nameof(IPage.CloseAction)) { Converter = new IActionToICommandConverter() });

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { designButton, viewButton, saveButton, cancelButton }
        };

        Content = new StackPanel
        {
            Children = { titleLabel, parameterList, mimeTypeRow, buttonRow }
        };

        // KeyBinding is a plain object, not part of the visual tree - binding its Command before
        // DataContext is actually set throws "Cannot find a DataContext to bind to" instead of
        // resolving lazily (the exact same bug Phase 2's PageWindow.cs already found), so this
        // waits for DataContextChanged rather than running in the constructor. Confirmed the hard
        // way that waiting for the event alone isn't enough either: the ambient DataContext the
        // binding engine resolves via visual-tree inheritance still isn't ready at the exact
        // moment the event fires, even though the DataContext property itself already reads the
        // new value - matches why PageWindow.cs's own version passes Source = DataContext
        // explicitly instead of relying on inheritance, which is copied here too.
        var keyBindingsAdded = false;
        DataContextChanged += (_, _) =>
        {
            if (keyBindingsAdded)
                return;
            keyBindingsAdded = true;

            KeyBindings.Add(new KeyBinding
            {
                Gesture = KeyGesture.Parse("Escape"),
                [!KeyBinding.CommandProperty] = new Binding(nameof(IPage.CloseAction)) { Converter = new IActionToICommandConverter(), Source = DataContext }
            });
        };

        Initialized += (_, _) => (DataContext as Page)?.OnLoadingAction.Execute();
        Loaded += (_, _) => (DataContext as Page)?.OnLoadedAction.Execute();
    }
}
