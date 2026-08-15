using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Kapok.View.Avalonia.Windows;

/// <summary>
/// Avalonia has no built-in MessageBox (unlike WPF). This is a minimal, code-built replacement
/// covering the four cases AvaloniaViewDomain.Show*Message needs. Plain C# construction rather
/// than .axaml - Phase 1 is about proving the ViewDomain contract works end to end, not chrome
/// polish; a themed version can replace this once Phase 2 brings the Ribbon/window styling in.
/// </summary>
public static class MessageBoxWindow
{
    public enum MessageBoxKind
    {
        Info,
        Error
    }

    public static void Show(string message, string title, MessageBoxKind kind, Window? owner)
    {
        ShowCore(message, title, new[] { "OK" }, owner);
    }

    public static bool ShowYesNo(string message, string title, Window? owner)
    {
        return ShowCore(message, title, new[] { "Yes", "No" }, owner) == "Yes";
    }

    public static bool ShowOkCancel(string message, string title, Window? owner)
    {
        return ShowCore(message, title, new[] { "OK", "Cancel" }, owner) == "OK";
    }

    private static string? ShowCore(string message, string title, string[] buttons, Window? owner)
    {
        string? clickedButton = null;

        var window = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new global::Avalonia.Thickness(0, 16, 0, 0)
        };

        foreach (var buttonText in buttons)
        {
            var button = new Button { Content = buttonText, MinWidth = 75 };
            button.Click += (_, _) =>
            {
                clickedButton = buttonText;
                window.Close();
            };
            buttonPanel.Children.Add(button);
        }

        window.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                buttonPanel
            }
        };

        // Same "block synchronously but keep the UI pumping" approach used throughout
        // AvaloniaViewDomain for bridging Avalonia's Task-based dialog APIs to the ViewDomain
        // contract's synchronous methods.
        var frame = new DispatcherFrame();
        window.Closed += (_, _) => frame.Continue = false;

        if (owner != null)
            window.Show(owner);
        else
            window.Show();

        Dispatcher.UIThread.PushFrame(frame);

        return clickedButton;
    }
}
