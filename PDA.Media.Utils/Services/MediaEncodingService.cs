using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Utils.Models;

namespace PDA.Media.Utils.Services;

public sealed record EncodeResult(EncodeStatus Status, string Message);

/// <summary>
/// Encodes one source file with an <see cref="EncodeProfile"/>.
/// The source is never modified. Output is written to "&lt;output&gt;.partial" and only replaces the real
/// output file (overwriting any existing one) once FFmpeg finishes successfully, so a failed or cancelled
/// encode never destroys an existing good file, and Plex never picks up a half-written one.
/// </summary>
public class MediaEncodingService
{
    // FFmpeg format names for the profile's container, needed because the ".partial" extension hides it.
    private static readonly Dictionary<string, string> FormatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mkv"] = "matroska",
        ["mp4"] = "mp4",
        ["m4v"] = "mp4",
        ["mov"] = "mov",
        ["webm"] = "webm",
        ["avi"] = "avi",
        ["ts"] = "mpegts"
    };

    private readonly ILogger<MediaEncodingService> _logger;

    public MediaEncodingService() : this(NullLogger<MediaEncodingService>.Instance)
    {
    }

    public MediaEncodingService(ILogger<MediaEncodingService> logger)
    {
        _logger = logger;
    }

    /// <param name="progress">Receives the percentage (0-100) of the current file.</param>
    public async Task<EncodeResult> EncodeAsync(string sourceFile, string outputFile, EncodeProfile profile,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var source = new FileInfo(sourceFile);
        if (!source.Exists)
        {
            _logger.LogError("Source file {SourceFile} not found", sourceFile);
            return new EncodeResult(EncodeStatus.Failed, "Source file not found");
        }

        if ((ulong)source.Length < profile.MinimumSourceFileSize)
        {
            string size = EncodeProfile.FormatFileSize((ulong)source.Length);
            _logger.LogInformation(
                "Skipped {SourceFile}: {FileSize} is below the profile's minimum of {MinimumSize} (too small or already encoded)",
                sourceFile, size, profile.MinimumSourceFileSizeFormatted);
            return new EncodeResult(EncodeStatus.Skipped, $"Skipped: {size} is below {profile.MinimumSourceFileSizeFormatted}");
        }

        string partialFile = outputFile + ".partial";
        var stopwatch = Stopwatch.StartNew();
        using var monitor = new FFmpegOutputMonitor(_logger, Path.GetFileName(sourceFile), StallWarningAfter);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            var analysis = await FFProbe.AnalyseAsync(sourceFile, cancellationToken: cancellationToken);
            LogSourceSummary(sourceFile, analysis);

            var job = FFMpegArguments
                .FromFileInput(sourceFile, verifyExists: true, options =>
                {
                    if (profile.GeneratedInputArguments.Length > 0) options.WithCustomArgument(profile.GeneratedInputArguments);
                })
                .OutputToFile(partialFile, overwrite: true, options => options
                    .WithCustomArgument(profile.GeneratedOutputArguments)
                    .ForceFormat(FormatNames.GetValueOrDefault(profile.ContainerFormat, profile.ContainerFormat)))
                .NotifyOnProgress(percent =>
                {
                    monitor.OnProgress(percent);
                    progress?.Report(Math.Clamp(percent, 0, 100));
                }, analysis.Duration)
                .NotifyOnError(monitor.OnOutputLine) // FFmpeg writes its log and status lines to stderr
                .CancellableThrough(cancellationToken);

            _logger.LogInformation("Encoding {SourceFile} to {OutputFile} ({Duration:hh\\:mm\\:ss}) with profile {ProfileName}",
                sourceFile, outputFile, analysis.Duration, profile.Name);
            _logger.LogInformation("ffmpeg {Arguments}", job.Arguments);

            await job.ProcessAsynchronously(throwOnError: true);
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(partialFile, outputFile, overwrite: true);
            long outputSize = new FileInfo(outputFile).Length;
            _logger.LogInformation("Encoded {OutputFile} in {Elapsed:hh\\:mm\\:ss}: {SourceSize} -> {OutputSize} ({Percent:0}% of the original)",
                outputFile, stopwatch.Elapsed, EncodeProfile.FormatFileSize((ulong)source.Length),
                EncodeProfile.FormatFileSize((ulong)outputSize), 100.0 * outputSize / source.Length);
            progress?.Report(100);
            return new EncodeResult(EncodeStatus.Done, "Done");
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            DeletePartial(partialFile);
            _logger.LogWarning("Cancelled encoding {SourceFile} after {Elapsed:hh\\:mm\\:ss} ({ExceptionType})",
                sourceFile, stopwatch.Elapsed, ex.GetType().Name);
            return new EncodeResult(EncodeStatus.Cancelled, "Cancelled");
        }
        catch (Exception ex)
        {
            DeletePartial(partialFile);
            _logger.LogError(ex, "Failed to encode {SourceFile}", sourceFile);
            _logger.LogError("Last FFmpeg output for {SourceFile}: {FFmpegMessages}", Path.GetFileName(sourceFile), monitor.RecentLines(15));
            return new EncodeResult(EncodeStatus.Failed, "Failed: " + FirstLine(ex.Message));
        }
    }

    /// <summary>How long progress may stand still before a warning is logged (FFmpeg keeps running).</summary>
    public static TimeSpan StallWarningAfter { get; set; } = TimeSpan.FromMinutes(3);

    // e.g. "Source Pilot.mkv: 00:44:12 (video 00:44:10); video: hevc 1920x1080; audio: truehd eng, ac3 eng; subtitles: hdmv_pgs_subtitle eng"
    private void LogSourceSummary(string sourceFile, IMediaAnalysis analysis)
    {
        static string Describe<T>(IEnumerable<T> streams, Func<T, string> describe) =>
            streams.Any() ? string.Join(", ", streams.Select(describe)) : "none";

        string video = Describe(analysis.VideoStreams, v => $"{v.CodecName} {v.Width}x{v.Height}" + (v.Duration > TimeSpan.Zero ? $" ({v.Duration:hh\\:mm\\:ss})" : ""));
        string audio = Describe(analysis.AudioStreams, a => $"{a.CodecName} {a.Language}".Trim() + (a.Duration > TimeSpan.Zero ? $" ({a.Duration:hh\\:mm\\:ss})" : ""));
        string subtitles = Describe(analysis.SubtitleStreams, s => $"{s.CodecName} {s.Language}".Trim());

        _logger.LogInformation("Source {SourceFile}: duration {Duration:hh\\:mm\\:ss}; video: {Video}; audio: {Audio}; subtitles: {Subtitles}",
            Path.GetFileName(sourceFile), analysis.Duration, video, audio, subtitles);
    }

    private void DeletePartial(string partialFile)
    {
        try
        {
            if (File.Exists(partialFile)) File.Delete(partialFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete the incomplete file {PartialFile}", partialFile);
        }
    }

    /// <summary>
    /// Watches FFmpeg's stderr: logs its warnings, errors and stream mapping, keeps recent lines for diagnostics,
    /// and logs a warning when progress stands still while FFmpeg keeps running.
    /// </summary>
    private sealed class FFmpegOutputMonitor : IDisposable
    {
        private const int MaxLoggedMessages = 20;
        private const int RecentLineCount = 40;
        private static readonly TimeSpan RepeatStallWarningEvery = TimeSpan.FromMinutes(10);
        private static readonly string[] ProblemWords =
            ["error", "warning", "invalid", "failed", "too many", "non monoton", "corrupt", "discard", "could not", "unable"];

        private readonly ILogger _logger;
        private readonly string _sourceName;
        private readonly TimeSpan _stallAfter;
        private readonly Timer _timer;
        private readonly object _lock = new();
        private readonly Queue<string> _recentLines = new();
        private string _lastStatus = "(none yet)";
        private int _loggedMessages;
        private double _lastPercent = -1;
        private DateTime _lastProgressAt = DateTime.UtcNow;
        private DateTime _lastStallWarningAt = DateTime.MinValue;

        public FFmpegOutputMonitor(ILogger logger, string sourceName, TimeSpan stallAfter)
        {
            _logger = logger;
            _sourceName = sourceName;
            _stallAfter = stallAfter;
            var interval = TimeSpan.FromSeconds(Math.Clamp(stallAfter.TotalSeconds / 4, 1, 30));
            _timer = new Timer(_ => CheckForStall(), null, interval, interval);
        }

        public void OnOutputLine(string line)
        {
            line = line.Trim();
            if (line.Length == 0) return;

            // Status lines ("frame= 1234 fps= 25 ... time=00:41:02.12 ... speed=1.2x") are only kept for diagnostics.
            if (line.StartsWith("frame=", StringComparison.Ordinal) || line.StartsWith("size=", StringComparison.Ordinal))
            {
                lock (_lock) _lastStatus = line;
                return;
            }

            lock (_lock)
            {
                _recentLines.Enqueue(line);
                while (_recentLines.Count > RecentLineCount) _recentLines.Dequeue();
            }

            bool isMapping = line.StartsWith("Stream #", StringComparison.Ordinal) && line.Contains("->", StringComparison.Ordinal);
            bool isProblem = ProblemWords.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase));
            if (!isMapping && !isProblem) return;

            int count = Interlocked.Increment(ref _loggedMessages);
            if (count <= MaxLoggedMessages)
            {
                if (isProblem) _logger.LogWarning("FFmpeg ({SourceFile}): {FFmpegMessage}", _sourceName, line);
                else _logger.LogInformation("FFmpeg mapping: {FFmpegMessage}", line);
            }
            else if (count == MaxLoggedMessages + 1)
            {
                _logger.LogWarning("Further FFmpeg messages for {SourceFile} are not logged", _sourceName);
            }
        }

        /// <summary>The last <paramref name="count"/> non-status lines FFmpeg wrote, joined with " | ".</summary>
        public string RecentLines(int count)
        {
            lock (_lock) return _recentLines.Count == 0 ? "(none)" : string.Join(" | ", _recentLines.TakeLast(count));
        }

        public void OnProgress(double percent)
        {
            lock (_lock)
            {
                if (percent <= _lastPercent) return;
                _lastPercent = percent;
                _lastProgressAt = DateTime.UtcNow;
                _lastStallWarningAt = DateTime.MinValue;
            }
        }

        private void CheckForStall()
        {
            string status, recent;
            double percent;
            TimeSpan stalledFor;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                stalledFor = now - _lastProgressAt;
                if (stalledFor < _stallAfter || now - _lastStallWarningAt < RepeatStallWarningEvery) return;
                _lastStallWarningAt = now;
                status = _lastStatus;
                percent = Math.Max(_lastPercent, 0);
            }
            recent = RecentLines(10);

            _logger.LogWarning(
                "No progress on {SourceFile} for {StalledMinutes:0.#} minutes at {Percent:0.0}% (FFmpeg is still running). Last FFmpeg status: {FFmpegStatus}. Recent FFmpeg messages: {FFmpegMessages}",
                _sourceName, stalledFor.TotalMinutes, percent, status, recent);
        }

        public void Dispose() => _timer.Dispose();
    }

    private static string FirstLine(string text)
    {
        int newline = text.IndexOfAny(['\r', '\n']);
        return newline > 0 ? text[..newline] : text;
    }
}
