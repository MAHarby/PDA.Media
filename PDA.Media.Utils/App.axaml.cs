using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDA.Media.Utils.ViewModels;
using PDA.Media.Utils.Views;

namespace PDA.Media.Utils;

public partial class App : Application
{
    /// <summary>
    /// The application's DI container, created in <see cref="Program.Main"/>.
    /// Null when running in the IDE previewer.
    /// </summary>
    public static IServiceProvider? Services { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Services != null)
            {
                var logger = Services.GetRequiredService<ILogger<App>>();
                logger.LogInformation("Avalonia framework initialised; creating main window");

                desktop.MainWindow = Services.GetRequiredService<MainWindow>();
                desktop.ShutdownRequested += (_, _) => logger.LogInformation("Application shutdown requested");
            }
            else
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainViewModel(),
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
