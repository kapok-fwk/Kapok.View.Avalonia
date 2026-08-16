using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Kapok.BusinessLayer;
using Kapok.Data;
using Kapok.Data.EntityFrameworkCore;
using Kapok.Module;
using Kapok.View;
using Kapok.View.Avalonia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ToDoAvaloniaApp.View;

namespace ToDoAvaloniaApp;

public class App : Application
{
    public IHost Host { get; private set; } = null!;

    public T GetService<T>() where T : class
    {
        if (Host.Services.GetService(typeof(T)) is not T service)
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices.");

        return service;
    }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ModuleEngine.InitiateModule(typeof(ToDoModule));

        DataDomain.DefaultEntityServiceType = typeof(EntityDeferredCommitService<>);

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .UseDefaultServiceProvider((_, options) => { options.ValidateOnBuild = true; })
            .ConfigureServices((_, services) =>
            {
                // View logic
                services.AddSingleton<IViewDomain, AvaloniaViewDomain>(serviceProvider =>
                    new AvaloniaViewDomain(ShutdownApplication, serviceProvider));

                // Data logic
                services.AddSingleton<IDataDomain>(serviceProvider =>
                {
                    var optionsBuilder = new DbContextOptionsBuilder();
                    optionsBuilder.UseInMemoryDatabase("ToDos");

                    return new EFCoreDataDomain(optionsBuilder.Options)
                    {
                        ServiceProvider = serviceProvider
                    };
                });
                services.TryAdd(ServiceDescriptor.Scoped<IDataDomainScope>(p =>
                    new EFCoreDataDomainScope(p.GetRequiredService<IDataDomain>(), p)));
                services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(EFCoreRepository<>)));

                // Views and ViewModels
                services.AddTransient<MainPage>();
                services.AddTransient<TaskLists>();
                services.AddTransient<Tasks>();
                services.AddTransient<TestPage>();
            }).Build();

        // Verification-only hook (see Program.cs's KAPOK_HEADLESS_SCREENSHOT): lets a headless
        // screenshot target a page other than MainPage, e.g. "TaskLists" - MainPage's own Base
        // menu is currently empty by design (its actions target a separate "Main" menu meant for
        // a navigation-bar surface that doesn't exist until Phase 3's DocumentPageCollectionPage
        // lands, see MainPage.cs), so it alone can't prove the Ribbon renders real buttons.
        var pageTypeName = Environment.GetEnvironmentVariable("KAPOK_HEADLESS_SCREENSHOT_PAGE");
        IPage page = pageTypeName switch
        {
            "TaskLists" => GetService<TaskLists>(),
            "Tasks" => GetService<Tasks>(),
            "TestPage" => GetService<TestPage>(),
            _ => GetService<MainPage>()
        };
        page.Show();

        base.OnFrameworkInitializationCompleted();
    }

    private void ShutdownApplication(int exitCode)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown(exitCode);
    }
}
