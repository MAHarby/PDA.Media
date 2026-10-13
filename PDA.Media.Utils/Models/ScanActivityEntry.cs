using System;
using Microsoft.Extensions.Logging;
using PDA.Media.Scanner.Services;

namespace PDA.Media.Utils.Models;

/// <summary>A line in the media scanner's activity log: a message, or a row the scan changed.</summary>
public sealed class ScanActivityEntry
{
    public ScanActivityEntry(DateTime timestamp, LogLevel level, string text, MediaScanChange? change = null)
    {
        Timestamp = timestamp;
        Level = level;
        Text = text;
        Change = change;
    }

    public DateTime Timestamp { get; }
    public LogLevel Level { get; }
    public string Text { get; }
    public MediaScanChange? Change { get; }

    /// <summary>The changed row's folder, shown as a tooltip.</summary>
    public string? Folder => Change?.Folder;

    public bool IsWarning => Level == LogLevel.Warning;
    public bool IsError => Level >= LogLevel.Error;
    public bool IsAdded => Change?.Kind is MediaScanChangeKind.Added or MediaScanChangeKind.Restored;
    public bool IsDeleted => Change?.Kind == MediaScanChangeKind.Deleted;

    public string DisplayText => $"{Timestamp:HH:mm:ss} {Text}";
}
