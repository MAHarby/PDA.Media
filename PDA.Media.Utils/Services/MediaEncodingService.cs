using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            var analysis = await FFProbe.AnalyseAsync(sourceFile, cancellationToken: cancellationToken);

            var job = FFMpegArguments
                .FromFileInput(sourceFile, verifyExists: true, options =>
                {
                    if (profile.GeneratedInputArguments.Length > 0) options.WithCustomArgument(profile.GeneratedInputArguments);
                })
                .OutputToFile(partialFile, overwrite: true, options => options
                    .WithCustomArgument(profile.GeneratedOutputArguments)
                    .ForceFormat(FormatNames.GetValueOrDefault(profile.ContainerFormat, profile.ContainerFormat)))
                .NotifyOnProgress(percent => progress?.Report(Math.Clamp(percent, 0, 100)), analysis.Duration)
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
            return new EncodeResult(EncodeStatus.Failed, "Failed: " + FirstLine(ex.Message));
        }
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

    private static string FirstLine(string text)
    {
        int newline = text.IndexOfAny(['\r', '\n']);
        return newline > 0 ? text[..newline] : text;
    }
}
