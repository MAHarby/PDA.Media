using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia;
using Avalonia.Threading;
using Serilog.Core;
using Serilog.Events;

namespace PDA.Media.Utils.Logging;

/// <summary>
/// Serilog sink that feeds log events into <see cref="Entries"/> for the Audit Log panel.
/// Events can arrive on any thread; they are queued and added to the collection on the UI thread.
/// </summary>
public sealed class AuditLogSink : ILogEventSink
{
    /// <summary>Oldest entries are dropped beyond this count to keep the panel responsive.</summary>
    public const int MaxEntries = 2000;

    private readonly ConcurrentQueue<AuditLogEntry> _pending = new();
    private int _drainScheduled;

    /// <summary>Entries for binding. Only modified on the UI thread.</summary>
    public ObservableCollection<AuditLogEntry> Entries { get; } = new();

    public void Emit(LogEvent logEvent)
    {
        _pending.Enqueue(AuditLogEntry.FromLogEvent(logEvent));

        // Before Avalonia is running (early startup, unit tests) events stay queued
        // and are drained by the first event logged once the UI thread is available.
        if (Application.Current == null) return;

        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
        {
            Dispatcher.UIThread.Post(DrainPending, DispatcherPriority.Background);
        }
    }

    /// <summary>Clears the panel's entries. The log file is not affected. Call on the UI thread.</summary>
    public void Clear() => Entries.Clear();

    private void DrainPending()
    {
        Interlocked.Exchange(ref _drainScheduled, 0);

        while (_pending.TryDequeue(out var entry))
        {
            Entries.Add(entry);
        }

        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }
    }
}
