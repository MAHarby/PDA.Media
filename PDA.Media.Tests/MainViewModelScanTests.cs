using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Logging;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

/// <summary>The main window's Scan button, which opens the media scanner (MediaScannerViewModelTests cover the scanner window).</summary>
[TestClass]
public sealed class MainViewModelScanTests
{
    [TestMethod]
    public void TestNoScanButtonWithoutAScanner()
    {
        using var temp = new TempServices();

        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService);

        Assert.IsFalse(vm.IsMediaScannerAvailable);
        Assert.IsFalse(vm.CanOpenMediaScanner);
    }

    [TestMethod]
    public async Task TestScanButtonIsEnabledOnceTheDatabaseIsOnline()
    {
        using var db = new SqlServerTestDatabase();
        using var temp = new TempServices();

        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService, new AuditLogSink(), new FFmpegService(), new MediaEncodingService(),
            NullLogger<MainViewModel>.Instance, new DatabaseStatusService(db.ContextFactory), Scanner(db.ContextFactory));

        Assert.IsTrue(vm.IsMediaScannerAvailable);
        await WaitUntil(() => vm.DatabaseState is not (DatabaseState.Checking or DatabaseState.NotConfigured));
        Assert.AreEqual(DatabaseState.Online, vm.DatabaseState);
        Assert.IsTrue(vm.CanOpenMediaScanner);
        StringAssert.Contains(vm.ScanMediaToolTip, "Open the media scanner");
    }

    [TestMethod]
    public async Task TestScanButtonIsDisabledWhileTheDatabaseIsOffline()
    {
        using var temp = new TempServices();
        var factory = new Data.Contexts.DataContextFactory(Data.DataConnection.BuildConnectionString("localhost,1", "Media.Master"));

        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService, new AuditLogSink(), new FFmpegService(), new MediaEncodingService(),
            NullLogger<MainViewModel>.Instance, new DatabaseStatusService(factory), Scanner(factory));
        await WaitUntil(() => vm.DatabaseState is not (DatabaseState.Checking or DatabaseState.NotConfigured));

        Assert.AreEqual(DatabaseState.Offline, vm.DatabaseState);
        Assert.IsTrue(vm.IsMediaScannerAvailable);
        Assert.IsFalse(vm.CanOpenMediaScanner);
        StringAssert.Contains(vm.ScanMediaToolTip, "needs the database");
    }

    private static MediaScanner Scanner(Microsoft.EntityFrameworkCore.IDbContextFactory<Data.Contexts.DataContext> factory) =>
        new(new MediaCategoryService(factory), new ArtistService(factory), new AlbumService(factory), new AlbumTypeService(factory), new TrackService(factory),
            new MovieService(factory), new TVShowService(factory), new TVShowEpisodeService(factory), new MediaCacheService(factory), new SettingService(factory));

    private static async Task WaitUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(50);
    }
}
