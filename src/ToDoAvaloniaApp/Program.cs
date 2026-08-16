using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;

namespace ToDoAvaloniaApp;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // See ToDoAvaloniaApp.csproj's Avalonia.Headless comment - lets phase verification
        // render and screenshot the real app without a live display/compositor. Set
        // KAPOK_HEADLESS_SCREENSHOT to an output file path to use this instead of a normal run.
        var screenshotPath = Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT");
        if (screenshotPath != null)
        {
            RunHeadlessScreenshot(screenshotPath);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void RunHeadlessScreenshot(string screenshotPath)
    {
        // lifetime.Windows isn't populated at Setup time in this Avalonia version (only once the
        // lifetime is actually Start()-ed, which we don't want here) - a class handler on
        // Window's own WindowOpenedEvent catches the real window App.OnFrameworkInitializationCompleted
        // opens via mainPage.Show(), regardless of that wiring. Tracks the *last* window opened,
        // not just the first: Phase 5's KAPOK_HEADLESS_SCREENSHOT_PAGE=TaskCard flow shows Tasks
        // and then triggers CreateNewEntryAction, which opens a second, TaskCard dialog window on
        // top of it (via Tasks.OpenCardPageAction) - that's the one worth capturing. For every
        // other page (still just one window ever opened), this behaves identically to the old
        // first-window-wins logic.
        Window? openedWindow = null;
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => openedWindow = w);

        var appBuilder = AppBuilder.Configure<App>()
            .UseSkia()
            // UseHeadlessDrawing defaults to true (layout/logic only, no real pixels) - false
            // switches to real Skia software rendering so CaptureRenderedFrame returns an actual
            // bitmap instead of null.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .LogToTrace();

        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = Array.Empty<string>(),
            ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown
        };
        appBuilder.SetupWithLifetime(lifetime);

        // OnFrameworkInitializationCompleted (App.OnFrameworkInitializationCompleted) already
        // called mainPage.Show() as part of Setup - give the dispatcher a few passes to let
        // layout/render actually run (window construction, Ribbon build, template resolution)
        // before capturing, same as a real compositor would need a frame or two.
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        var window = openedWindow;
        if (window == null)
        {
            Console.WriteLine("KAPOK_HEADLESS_SCREENSHOT: no window was shown.");
            Environment.Exit(1);
        }

        using var frame = window!.CaptureRenderedFrame();
        if (frame == null)
        {
            Console.WriteLine("KAPOK_HEADLESS_SCREENSHOT: CaptureRenderedFrame returned null.");
            Environment.Exit(1);
        }

        frame!.Save(screenshotPath);
        Console.WriteLine($"KAPOK_HEADLESS_SCREENSHOT: saved {frame.PixelSize} to {screenshotPath}");
        lifetime.Shutdown();
    }
}
