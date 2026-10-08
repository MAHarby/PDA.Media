using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Utils.Models;

namespace PDA.Media.Utils.Services;

/// <param name="OutputFile">The file written, when it differs from the requested output (a copy keeps the source's extension).</param>
public sealed record EncodeResult(EncodeStatus Status, string Message, string? OutputFile = null);

/// <summary>
/// Encodes one source file with an <see cref="EncodeProfile"/>.
/// The source is never modified. Output is written to "&lt;output&gt;.partial" and only replaces the real
/// output file (overwriting any existing one) once FFmpeg finishes successfully, so a failed or cancelled
/// encode never destroys an existing good file, and Plex never picks up a half-written one.
/// </summary>
public partial class MediaEncodingService
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

        // Below the minimum size the file is too small to be worth encoding (or already encoded). It's copied across
        // unchanged, under its Plex name, unless it's already at the destination.
        if ((ulong)source.Length < profile.MinimumSourceFileSize)
        {
            string size = EncodeProfile.FormatFileSize((ulong)source.Length);
            // A copy isn't re-encoded, so it keeps the source's own extension (e.g. .mp4) rather than the profile's.
            string copyFile = source.Extension.Length > 0 ? Path.ChangeExtension(outputFile, source.Extension) : outputFile;
            string? existing = File.Exists(copyFile) ? copyFile : File.Exists(outputFile) ? outputFile : null;

            if (existing != null)
            {
                _logger.LogInformation(
                    "Skipped {SourceFile}: {FileSize} is below the profile's minimum of {MinimumSize} and {ExistingFile} already exists",
                    sourceFile, size, profile.MinimumSourceFileSizeFormatted, existing);
                return new EncodeResult(EncodeStatus.Skipped, "Skipped: below minimum, already at destination", existing);
            }

            _logger.LogInformation(
                "{SourceFile}: {FileSize} is below the profile's minimum of {MinimumSize}, so it is copied instead of encoded",
                sourceFile, size, profile.MinimumSourceFileSizeFormatted);
            return await CopyAsync(source, copyFile, progress, cancellationToken);
        }

        string partialFile = outputFile + ".partial";
        var stopwatch = Stopwatch.StartNew();
        using var monitor = new FFmpegOutputMonitor(_logger, Path.GetFileName(sourceFile), StallWarningAfter);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            var analysis = await FFProbe.AnalyseAsync(sourceFile, cancellationToken: cancellationToken);
            LogSourceSummary(sourceFile, analysis);

            // FFmpeg's "time=" follows whichever output stream is furthest along. Copied audio and subtitle streams
            // can run far ahead of the video being encoded, so progress is measured in video frames when possible.
            double expectedFrames = ExpectedVideoFrames(analysis);
            if (expectedFrames > 0)
            {
                monitor.TrackFrames(expectedFrames, percent => progress?.Report(percent));
                _logger.LogInformation("Progress is measured in video frames: about {ExpectedFrames:N0}", expectedFrames);
            }
            else
            {
                _logger.LogInformation("Video frame count unknown; progress follows FFmpeg's reported time");
            }

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
                    if (monitor.TracksFrames) return;
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

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="outputFile"/> through a ".partial" file, reporting progress.
    /// </summary>
    private async Task<EncodeResult> CopyAsync(FileInfo source, string outputFile, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        string partialFile = outputFile + ".partial";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

            const int bufferSize = 1024 * 1024;
            await using (var input = new FileStream(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true))
            await using (var output = new FileStream(partialFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true))
            {
                var buffer = new byte[bufferSize];
                long copied = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    copied += read;
                    if (source.Length > 0) progress?.Report(Math.Min(99.9, 100.0 * copied / source.Length));
                }
            }

            File.Move(partialFile, outputFile, overwrite: true);
            _logger.LogInformation("Copied {SourceFile} to {OutputFile} in {Elapsed:hh\\:mm\\:ss}",
                source.FullName, outputFile, stopwatch.Elapsed);
            progress?.Report(100);
            return new EncodeResult(EncodeStatus.Copied, "Copied (below minimum size)", outputFile);
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            DeletePartial(partialFile);
            _logger.LogWarning("Cancelled copying {SourceFile} ({ExceptionType})", source.FullName, ex.GetType().Name);
            return new EncodeResult(EncodeStatus.Cancelled, "Cancelled");
        }
        catch (Exception ex)
        {
            DeletePartial(partialFile);
            _logger.LogError(ex, "Failed to copy {SourceFile} to {OutputFile}", source.FullName, outputFile);
            return new EncodeResult(EncodeStatus.Failed, "Copy failed: " + FirstLine(ex.Message));
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

    // Total video frames: mkvmerge's exact NUMBER_OF_FRAMES tag when present, otherwise frame rate x duration;
    // 0 when neither is known.
    public static double ExpectedVideoFrames(IMediaAnalysis analysis)
    {
        var video = analysis.PrimaryVideoStream;
        if (video == null) return 0;

        // The tag may carry a language suffix, e.g. "NUMBER_OF_FRAMES-eng".
        var frameTag = video.Tags?.FirstOrDefault(tag => tag.Key.StartsWith("NUMBER_OF_FRAMES", StringComparison.OrdinalIgnoreCase));
        if (frameTag?.Value is { } tagValue && long.TryParse(tagValue, out long taggedFrames) && taggedFrames > 0)
        {
            return taggedFrames;
        }

        double fps = video.AvgFrameRate > 0 ? video.AvgFrameRate : video.FrameRate;
        var duration = video.Duration > TimeSpan.Zero ? video.Duration : analysis.Duration;
        return fps > 0 && duration > TimeSpan.Zero ? fps * duration.TotalSeconds : 0;
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
    private sealed partial class FFmpegOutputMonitor : IDisposable
    {
        private const int MaxLoggedProblems = 20;
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
        private int _loggedProblems;
        private readonly List<string> _mappings = new();
        private bool _mappingLogged;
        private double _expectedFrames;
        private Action<double>? _reportFramePercent;

        [GeneratedRegex(@"^frame=\s*(\d+)")]
        private static partial Regex FrameCount { get; }

        public bool TracksFrames => _expectedFrames > 0;

        /// <summary>Reports progress as encoded frames / <paramref name="expectedFrames"/> from FFmpeg's status lines.</summary>
        public void TrackFrames(double expectedFrames, Action<double> report)
        {
            _expectedFrames = expectedFrames;
            _reportFramePercent = report;
        }
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

            // Status lines: "frame= 1234 fps= 25 ... time=00:41:02.12 ... speed=1.2x".
            if (line.StartsWith("frame=", StringComparison.Ordinal) || line.StartsWith("size=", StringComparison.Ordinal))
            {
                lock (_lock) _lastStatus = line;
                LogMappingOnce();

                var frames = FrameCount.Match(line);
                if (_expectedFrames > 0 && frames.Success)
                {
                    // Held just below 100% until FFmpeg has actually finished.
                    double percent = Math.Min(double.Parse(frames.Groups[1].Value) / _expectedFrames * 100, 99.9);
                    OnProgress(percent);
                    _reportFramePercent?.Invoke(percent);
                }
                return;
            }

            lock (_lock)
            {
                _recentLines.Enqueue(line);
                while (_recentLines.Count > RecentLineCount) _recentLines.Dequeue();
            }

            if (line.StartsWith("Stream #", StringComparison.Ordinal) && line.Contains("->", StringComparison.Ordinal))
            {
                lock (_lock) _mappings.Add(line["Stream ".Length..]);
                return;
            }

            if (!ProblemWords.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase))) return;

            int count = Interlocked.Increment(ref _loggedProblems);
            if (count <= MaxLoggedProblems)
            {
                _logger.LogWarning("FFmpeg ({SourceFile}): {FFmpegMessage}", _sourceName, line);
            }
            else if (count == MaxLoggedProblems + 1)
            {
                _logger.LogWarning("Further FFmpeg warnings for {SourceFile} are not logged", _sourceName);
            }
        }

        // One line for the whole stream mapping: re-encoded streams in full, copied streams counted,
        // e.g. "#0:0 -> #0:0 (hevc (native) -> hevc (libx265)); 26 streams copied".
        private void LogMappingOnce()
        {
            string summary;
            lock (_lock)
            {
                if (_mappingLogged || _mappings.Count == 0) return;
                _mappingLogged = true;
                var converted = _mappings.Where(m => !m.EndsWith("(copy)", StringComparison.Ordinal)).ToList();
                int copied = _mappings.Count - converted.Count;
                summary = string.Join("; ", converted) + (copied > 0 ? $"{(converted.Count > 0 ? "; " : "")}{copied} streams copied" : "");
            }

            _logger.LogInformation("FFmpeg stream mapping: {FFmpegMapping}", summary);
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
