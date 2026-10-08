using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PDA.Media.Utils.Logging;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace PDA.Media.Tests;

[TestClass]
public sealed class EncodingTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateFolder()
    {
        _root = Path.Combine(Path.GetTempPath(), "pda_encoding_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void DeleteFolder()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static EncodeProfile FastProfile(ulong minimumSize = 0) => new()
    {
        Name = "Test",
        ContainerFormat = "mkv",
        MinimumSourceFileSize = minimumSize,
        VideoCodec = "libx264",
        Preset = "ultrafast",
        Crf = 30,
        PixelFormat = "yuv420p",
        AudioCodec = "copy",
        KeepAllStreams = true,
        CopySubtitles = false
    };

    // Profile arguments.
    // ==================================================================================================

    [TestMethod]
    public void TestProfile_HardwareAccelerationIsAnInputArgument()
    {
        var profile = FastProfile();
        profile.HardwareAcceleration = "CUDA";

        Assert.AreEqual("-hwaccel cuda", profile.GeneratedInputArguments);
        Assert.DoesNotContain("-hwaccel", profile.GeneratedOutputArguments);
        Assert.StartsWith("-hwaccel cuda -map 0", profile.GeneratedFFMpegArguments, "The preview still shows the full command");
    }

    [TestMethod]
    public void TestProfile_NoHardwareAccelerationMeansNoInputArguments()
    {
        var profile = FastProfile();
        profile.HardwareAcceleration = "None";

        Assert.AreEqual(string.Empty, profile.GeneratedInputArguments);
        Assert.AreEqual(profile.GeneratedOutputArguments, profile.GeneratedFFMpegArguments);
    }

    // Encoding service without FFmpeg.
    // ==================================================================================================

    [TestMethod]
    public async Task TestEncode_SkipsFilesBelowTheMinimumSize()
    {
        string source = Path.Combine(_root, "small.mkv");
        await File.WriteAllBytesAsync(source, new byte[1000]);
        string output = Path.Combine(_root, "out", "small.mkv");

        var result = await new MediaEncodingService().EncodeAsync(source, output, FastProfile(minimumSize: 1024 * 1024));

        Assert.AreEqual(EncodeStatus.Skipped, result.Status);
        Assert.Contains("below 1 MB", result.Message);
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(output)), "Nothing is written for a skipped file");
    }

    [TestMethod]
    public async Task TestEncode_MissingSourceFails()
    {
        var result = await new MediaEncodingService().EncodeAsync(
            Path.Combine(_root, "missing.mkv"), Path.Combine(_root, "out.mkv"), FastProfile());

        Assert.AreEqual(EncodeStatus.Failed, result.Status);
    }

    // Encoding service with FFmpeg (inconclusive when FFmpeg isn't installed).
    // ==================================================================================================

    private string CreateSampleVideo(string extraArguments = "")
    {
        if (!new FFmpegService(Path.Combine(_root, "no-binaries-here")).Locate())
        {
            Assert.Inconclusive("FFmpeg isn't on the PATH, so real encoding can't be tested here.");
        }

        string source = Path.Combine(_root, "sample.mkv");
        using var process = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-loglevel error -y -f lavfi -i testsrc2=duration=2:size=320x240:rate=24 -f lavfi -i sine=duration=2 -c:v libx264 -preset ultrafast -c:a aac {extraArguments} \"{source}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        process.WaitForExit(30000);
        Assert.IsTrue(File.Exists(source), "Sample video was created");
        return source;
    }

    [TestMethod]
    public async Task TestEncode_OverwritesExistingOutputAndLeavesNoPartialFile()
    {
        string source = CreateSampleVideo();
        long sourceSize = new FileInfo(source).Length;
        string output = Path.Combine(_root, "out", "Show", "Season 01", "Show - s01e01.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, "old file");
        double lastProgress = -1;

        var result = await new MediaEncodingService().EncodeAsync(source, output, FastProfile(),
            new SynchronousProgress(p => lastProgress = p));

        Assert.AreEqual(EncodeStatus.Done, result.Status, result.Message);
        Assert.IsGreaterThan(100L, new FileInfo(output).Length, "The old 8-byte file was replaced");
        Assert.IsFalse(File.Exists(output + ".partial"));
        Assert.AreEqual(sourceSize, new FileInfo(source).Length, "The source is untouched");
        Assert.AreEqual(100, lastProgress);
    }

    [TestMethod]
    public async Task TestEncode_CancelledKeepsExistingOutput()
    {
        string source = CreateSampleVideo();
        string output = Path.Combine(_root, "out.mkv");
        await File.WriteAllTextAsync(output, "existing good file");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await new MediaEncodingService().EncodeAsync(source, output, FastProfile(), null, cancellation.Token);

        Assert.AreEqual(EncodeStatus.Cancelled, result.Status);
        Assert.AreEqual("existing good file", await File.ReadAllTextAsync(output));
        Assert.IsFalse(File.Exists(output + ".partial"));
    }

    [TestMethod]
    public async Task TestExpectedFrames_UsesMkvmergeFrameCountTag()
    {
        // mkvmerge stores the exact count; here it deliberately differs from 2s x 24fps = 48.
        string source = CreateSampleVideo("-metadata:s:v:0 NUMBER_OF_FRAMES=40");

        var analysis = await FFMpegCore.FFProbe.AnalyseAsync(source);

        Assert.AreEqual(40, MediaEncodingService.ExpectedVideoFrames(analysis));
    }

    [TestMethod]
    public async Task TestExpectedFrames_FallsBackToFrameRateTimesDuration()
    {
        string source = CreateSampleVideo();

        var analysis = await FFMpegCore.FFProbe.AnalyseAsync(source);

        Assert.AreEqual(48, MediaEncodingService.ExpectedVideoFrames(analysis), 1.5, "2 seconds at 24 fps");
    }

    [TestMethod]
    public async Task TestEncode_ProgressOnlyMovesForwardAndEndsAt100()
    {
        string source = CreateSampleVideo();
        var reports = new System.Collections.Generic.List<double>();

        var result = await new MediaEncodingService().EncodeAsync(source, Path.Combine(_root, "progress.mkv"), FastProfile(),
            new SynchronousProgress(reports.Add));

        Assert.AreEqual(EncodeStatus.Done, result.Status, result.Message);
        CollectionAssert.AreEqual(reports.OrderBy(r => r).ToList(), reports, "Progress never goes backwards");
        Assert.AreEqual(100, reports[^1]);
        Assert.IsTrue(reports.Take(reports.Count - 1).All(r => r < 100), "100% is only reported once FFmpeg has finished");
    }

    // Main window queue.
    // ==================================================================================================

    [TestMethod]
    public void TestQueue_ShowsPlexOutputNamesUnderTheDestination()
    {
        using var temp = new TempServices();
        string season = Path.Combine(temp.SourceFolder, "Show (2020)", "Season 1");
        Directory.CreateDirectory(season);
        File.WriteAllText(Path.Combine(season, "Show (2020) - S01E02 - Second Bluray-1080p.mkv"), "x");
        string destination = Path.Combine(_root, "plex");
        temp.SettingsService.SaveSettings(new UserSettings
        {
            GeneralProfile = "", EncoderProfile = "Bluray TV", SourcePath = temp.SourceFolder, DestinationPath = destination
        });

        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService, new AuditLogSink(),
            NullLogger<MainViewModel>.Instance);
        vm.SourceMediaNodes[0].Selected = true;
        vm.LoadDestinationItemsCommand.Execute(null);

        var item = vm.DestinationItems.Single();
        string expected = Path.Combine("Show (2020)", "Season 01", "Show (2020) - s01e02 - Second.mkv");
        Assert.AreEqual(expected, item.OutputRelativePath);
        Assert.AreEqual(Path.Combine(destination, expected), item.OutputPath);
        Assert.AreEqual(EncodeStatus.Queued, item.Status);
        Assert.AreEqual(1, item.SourceSize, "The file holds one byte");
        Assert.AreEqual("- 1 B", item.SourceSizeText);
    }

    [TestMethod]
    [DataRow(13421772800L, "12.5 GB")]
    [DataRow(1073741824L, "1.0 GB")]
    [DataRow(891289600L, "850 MB")]
    [DataRow(12345L, "12 KB")]
    [DataRow(500L, "500 B")]
    public void TestQueue_SourceSizeFormatting(long bytes, string expected)
    {
        Assert.AreEqual(expected, DestinationItem.FormatSize(bytes));
    }

    // Progress<T> posts to the thread pool, so reports could arrive after the test asserts.
    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
