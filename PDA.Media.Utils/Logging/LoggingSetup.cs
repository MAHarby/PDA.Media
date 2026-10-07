using System;
using System.IO;
using System.Linq;
using PDA.Media.Utils.Services;
using Serilog;
using Serilog.Events;

namespace PDA.Media.Utils.Logging;

/// <summary>
/// Builds the application's Serilog logger: console, a new timestamped file per run, and the Audit Log panel.
/// </summary>
public static class LoggingSetup
{
    public const string LogFilePrefix = "PDA.Media.Utils_";
    public const int RetainedLogFileCount = 30;

    public static readonly string DefaultLogDirectory = Path.Combine(AppSettingsService.DefaultSettingsDirectory, "Logs");

    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Creates the logger. Each run writes to a new file named with the start time, e.g.
    /// <c>PDA.Media.Utils_20261007_153012.log</c>, so the log starts empty every time the app runs.
    /// </summary>
    public static ILogger CreateLogger(AuditLogSink auditLogSink, out string logFilePath, string? logDirectory = null)
    {
        logDirectory ??= DefaultLogDirectory;
        Directory.CreateDirectory(logDirectory);
        logFilePath = GetLogFilePath(logDirectory, DateTime.Now);
        DeleteOldLogFiles(logDirectory, RetainedLogFileCount - 1);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: OutputTemplate)
            .WriteTo.File(logFilePath, outputTemplate: OutputTemplate, shared: false)
            .WriteTo.Sink(auditLogSink, LogEventLevel.Information)
            .CreateLogger();
    }

    public static string GetLogFilePath(string logDirectory, DateTime startTime) =>
        Path.Combine(logDirectory, $"{LogFilePrefix}{startTime:yyyyMMdd_HHmmss}.log");

    /// <summary>
    /// Keeps the newest <paramref name="keepCount"/> log files and deletes the rest.
    /// </summary>
    public static void DeleteOldLogFiles(string logDirectory, int keepCount)
    {
        if (!Directory.Exists(logDirectory)) return;

        var oldFiles = new DirectoryInfo(logDirectory)
            .GetFiles($"{LogFilePrefix}*.log")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(Math.Max(keepCount, 0));

        foreach (var file in oldFiles)
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
                // A file still held open (e.g. by another running instance) is left for next time.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
