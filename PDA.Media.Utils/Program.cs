using Avalonia;
using System;
using System.Threading.Tasks;
using PDA.Media.Utils.Logging;
using Serilog;

namespace PDA.Media.Utils;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var auditLogSink = new AuditLogSink();
        Log.Logger = LoggingSetup.CreateLogger(auditLogSink, out string logFilePath);
        var log = Log.ForContext<Program>();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            log.Error(e.Exception, "Unobserved task exception");

        try
        {
            log.Information("PDA.Media.Utils starting (version {Version}, {OS}, .NET {Runtime})",
                typeof(Program).Assembly.GetName().Version, Environment.OSVersion, Environment.Version);
            log.Information("Logging to {LogFilePath}", logFilePath);

            using var services = ServiceConfiguration.BuildServiceProvider(auditLogSink);
            App.Services = services;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            log.Information("PDA.Media.Utils shut down normally");
        }
        catch (Exception ex)
        {
            log.Fatal(ex, "PDA.Media.Utils terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
