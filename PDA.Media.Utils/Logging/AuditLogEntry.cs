using System;
using System.IO;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace PDA.Media.Utils.Logging;

/// <summary>
/// A single log event as shown in the Audit Log panel on the main window.
/// </summary>
public sealed class AuditLogEntry
{
    // "lj" renders string properties without quotes, matching the file output.
    private static readonly MessageTemplateTextFormatter MessageFormatter = new("{Message:lj}");

    public DateTimeOffset Timestamp { get; }
    public LogEventLevel Level { get; }
    public string Source { get; }
    public string Message { get; }

    public bool IsWarning => Level == LogEventLevel.Warning;
    public bool IsError => Level >= LogEventLevel.Error;

    public string LevelText => Level switch
    {
        LogEventLevel.Verbose => "VRB",
        LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        _ => "FTL"
    };

    public string DisplayText => $"{Timestamp:HH:mm:ss} [{LevelText}] {Source}: {Message}";

    public AuditLogEntry(DateTimeOffset timestamp, LogEventLevel level, string source, string message)
    {
        Timestamp = timestamp;
        Level = level;
        Source = source;
        Message = message;
    }

    public static AuditLogEntry FromLogEvent(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        MessageFormatter.Format(logEvent, writer);
        string message = writer.ToString();
        if (logEvent.Exception != null)
        {
            message += $" ({logEvent.Exception.GetType().Name}: {logEvent.Exception.Message})";
        }

        return new AuditLogEntry(logEvent.Timestamp, logEvent.Level, GetShortSource(logEvent), message);
    }

    // Reduces "PDA.Media.Utils.ViewModels.MainViewModel" to "MainViewModel".
    private static string GetShortSource(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue("SourceContext", out var value) &&
            value is ScalarValue { Value: string sourceContext })
        {
            int lastDot = sourceContext.LastIndexOf('.');
            return lastDot >= 0 ? sourceContext[(lastDot + 1)..] : sourceContext;
        }

        return "App";
    }
}
