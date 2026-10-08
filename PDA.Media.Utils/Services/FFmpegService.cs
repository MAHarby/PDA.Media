using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Extensions.Downloader;
using FFMpegCore.Extensions.Downloader.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PDA.Media.Utils.Services;

/// <summary>
/// Finds the ffmpeg and ffprobe binaries and points FFMpegCore at them, or downloads them when missing.
/// Looks in the app's own <c>bin</c> folder first (next to the executable, like BatchConverter's <c>./bin</c>),
/// then on the PATH.
/// </summary>
public class FFmpegService
{
    public static readonly string DefaultBinaryFolder = Path.Combine(AppContext.BaseDirectory, "bin");

    private static readonly string FFmpegFileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
    private static readonly string FFprobeFileName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

    private readonly ILogger<FFmpegService> _logger;

    /// <summary>The app's own binaries folder; downloads go here.</summary>
    public string BinaryFolder { get; }

    /// <summary>The folder the binaries were found in, or null when they weren't found.</summary>
    public string? ActiveFolder { get; private set; }

    public bool IsAvailable => ActiveFolder != null;

    public FFmpegService(string? binaryFolder = null) : this(NullLogger<FFmpegService>.Instance, binaryFolder)
    {
    }

    public FFmpegService(ILogger<FFmpegService> logger, string? binaryFolder = null)
    {
        _logger = logger;
        BinaryFolder = binaryFolder ?? DefaultBinaryFolder;
    }

    /// <summary>
    /// Looks for ffmpeg and ffprobe and configures FFMpegCore to use them.
    /// </summary>
    /// <returns>True when both were found.</returns>
    public bool Locate()
    {
        string? folder = HasBinaries(BinaryFolder) ? BinaryFolder : FindOnPath();
        ActiveFolder = folder;

        if (folder == null)
        {
            _logger.LogWarning(
                "FFmpeg not found in {BinaryFolder} or on the PATH. Copy ffmpeg and ffprobe there, or use Download FFmpeg",
                BinaryFolder);
            return false;
        }

        GlobalFFOptions.Configure(options => options.BinaryFolder = folder);
        _logger.LogInformation("Using {FFmpegVersion} from {FFmpegFolder}", GetVersion(folder), folder);
        return true;
    }

    /// <summary>
    /// Downloads ffmpeg and ffprobe (via ffbinaries.com) into <see cref="BinaryFolder"/>, then locates them.
    /// </summary>
    public async Task<bool> DownloadAsync()
    {
        _logger.LogInformation("Downloading FFmpeg into {BinaryFolder}", BinaryFolder);
        Directory.CreateDirectory(BinaryFolder);

        var files = await FFMpegDownloader.DownloadBinaries(
            FFMpegVersions.LatestAvailable,
            FFMpegBinaries.FFMpeg | FFMpegBinaries.FFProbe,
            new FFOptions { BinaryFolder = BinaryFolder });

        _logger.LogInformation("Downloaded {FileNames}", string.Join(", ", files.Select(Path.GetFileName)));
        return Locate();
    }

    private static bool HasBinaries(string folder) =>
        File.Exists(Path.Combine(folder, FFmpegFileName)) && File.Exists(Path.Combine(folder, FFprobeFileName));

    private static string? FindOnPath() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault(HasBinaries);

    // First line of "ffmpeg -version", e.g. "ffmpeg version 6.1 Copyright (c) ...", shortened to "ffmpeg version 6.1".
    private string GetVersion(string folder)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(folder, FFmpegFileName), "-version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            string? line = process?.StandardOutput.ReadLine();
            process?.WaitForExit(5000);
            int copyright = line?.IndexOf(" Copyright", StringComparison.Ordinal) ?? -1;
            return line is null ? "ffmpeg" : copyright > 0 ? line[..copyright] : line;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the FFmpeg version");
            return "ffmpeg";
        }
    }
}
