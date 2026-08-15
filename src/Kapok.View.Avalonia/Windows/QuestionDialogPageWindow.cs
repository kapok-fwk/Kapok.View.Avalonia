using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Kapok.View.Avalonia.ValueConverter;

namespace Kapok.View.Avalonia.Windows;

/// <summary>
/// Renders a QuestionDialogPage's Message + DialogButtons as a message + button row. Matches
/// Kapok.View.Wpf's QuestionDialogPageWindow.xaml. DataPage&lt;TEntry&gt;.OnClosing (unsaved-changes
/// prompt) and AskRevertChangesDueToValidationErrors both route through this.
/// </summary>
public class QuestionDialogPageWindow : Window
{
    public QuestionDialogPageWindow()
    {
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        var messageText = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        messageText.Bind(TextBlock.TextProperty, new Binding(nameof(QuestionDialogPage.Message)));

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new global::Avalonia.Thickness(0, 16, 0, 0)
        };
        buttonPanel.Bind(ItemsControl.DataContextProperty, new Binding()); // just to force re-evaluation when DataContext is set below

        Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Children = { messageText, buttonPanel }
        };

        this.Bind(TitleProperty, new Binding(nameof(IPage.Title)));

        DataContextChanged += (_, _) => RebuildButtons(buttonPanel);
    }

    private void RebuildButtons(StackPanel buttonPanel)
    {
        buttonPanel.Children.Clear();

        if (DataContext is not QuestionDialogPage page)
            return;

        foreach (var dialogButton in page.DialogButtons)
        {
            var button = new Button
            {
                Content = dialogButton.Label?.LanguageOrDefault(System.Globalization.CultureInfo.CurrentUICulture) ?? string.Empty,
                MinWidth = 75,
                IsEnabled = dialogButton.IsEnabled,
                IsDefault = dialogButton.IsDefault,
                IsCancel = dialogButton.IsCancel,
                Command = ActionCommand.ForGeneric(page.DialogButtonAction),
                CommandParameter = dialogButton
            };
            buttonPanel.Children.Add(button);
        }
    }
}
