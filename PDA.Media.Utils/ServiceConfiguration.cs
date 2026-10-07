using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDA.Media.Utils.Logging;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;
using PDA.Media.Utils.Views;
using Serilog;

namespace PDA.Media.Utils;

/// <summary>
/// Dependency injection registrations for the application.
/// </summary>
public static class ServiceConfiguration
{
    public static ServiceProvider BuildServiceProvider(AuditLogSink auditLogSink, string logFilePath)
    {
        var services = new ServiceCollection();

        // Microsoft.Extensions.Logging ILogger<T> backed by the static Serilog logger configured in Program.
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSerilog(dispose: false);
        });

        services.AddSingleton(auditLogSink);

        // Services.
        services.AddSingleton(sp => new AppSettingsService(sp.GetRequiredService<ILogger<AppSettingsService>>()));
        services.AddSingleton(sp => new EncoderProfileService(sp.GetRequiredService<ILogger<EncoderProfileService>>()));
        services.AddSingleton(sp => new LogFileService(logFilePath, sp.GetRequiredService<ILogger<LogFileService>>()));

        // View models.
        services.AddTransient<MainViewModel>();
        services.AddTransient<Func<string?, EncoderProfilesViewModel>>(sp => selectedProfileName =>
            new EncoderProfilesViewModel(
                sp.GetRequiredService<EncoderProfileService>(),
                sp.GetRequiredService<ILogger<EncoderProfilesViewModel>>(),
                selectedProfileName));
        services.AddTransient<LogViewerViewModel>();
        services.AddTransient<Func<LogViewerViewModel>>(sp => sp.GetRequiredService<LogViewerViewModel>);

        // Views.
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
