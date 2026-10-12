using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Logging;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

/// <summary>The main window's Scan button (MainViewModel.ScanMediaCommand).</summary>
[TestClass]
public sealed class MainViewModelScanTests
{
    [TestMethod]
    public void TestNoScanButtonWithoutAScanner()
    {
        using var temp = new TempServices();

        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService);

        Assert.IsFalse(vm.IsMediaScannerAvailable);
        Assert.IsFalse(vm.ScanMediaCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task TestScanButtonScansIntoTheDatabase()
    {
        using var db = new SqlServerTestDatabase();
        using var temp = new TempServices();
        string music = Path.Combine(temp.SourceFolder, "Music", "ABBA", "Gold");
        Directory.CreateDirectory(music);
        File.WriteAllBytes(Path.Combine(music, "01 - Dancing Queen.flac"), [0]);
        new MediaCategoryService(db.ContextFactory).AddRecord(new MediaCategory { Name = "Music", MediaClass = "Music", RootFolder = Path.Combine(temp.SourceFolder, "Music") });
        var scanner = new MediaScanner(new MediaCategoryService(db.ContextFactory), new ArtistService(db.ContextFactory), new AlbumService(db.ContextFactory),
            new AlbumTypeService(db.ContextFactory), new TrackService(db.ContextFactory), new MovieService(db.ContextFactory),
            new TVShowService(db.ContextFactory), new TVShowEpisodeService(db.ContextFactory), new MediaCacheService(db.ContextFactory));

        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService, new AuditLogSink(), new FFmpegService(), new MediaEncodingService(),
            NullLogger<MainViewModel>.Instance, new DatabaseStatusService(db.ContextFactory), scanner);

        // The button is there, and enabled once the database check finds the database.
        Assert.IsTrue(vm.IsMediaScannerAvailable);
        await WaitUntil(() => vm.DatabaseState is not (DatabaseState.Checking or DatabaseState.NotConfigured));
        Assert.AreEqual(DatabaseState.Online, vm.DatabaseState);
        Assert.IsTrue(vm.ScanMediaCommand.CanExecute(null));

        await vm.ScanMediaCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsScanning);
        Assert.AreEqual("Scan finished: 3 added, 0 restored, 0 updated, 0 deleted", vm.StatusText);
        Assert.AreEqual("Dancing Queen", new TrackService(db.ContextFactory).GetAllRecords().Single().Name);
    }

    [TestMethod]
    public async Task TestScanButtonIsDisabledWhileTheDatabaseIsOffline()
    {
        using var temp = new TempServices();
        var factory = new Data.Contexts.DataContextFactory(Data.DataConnection.BuildConnectionString("localhost,1", "Media.Master"));
        var scanner = new MediaScanner(new MediaCategoryService(factory), new ArtistService(factory), new AlbumService(factory), new AlbumTypeService(factory),
            new TrackService(factory), new MovieService(factory), new TVShowService(factory), new TVShowEpisodeService(factory), new MediaCacheService(factory));

        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService, new AuditLogSink(), new FFmpegService(), new MediaEncodingService(),
            NullLogger<MainViewModel>.Instance, new DatabaseStatusService(factory), scanner);
        await WaitUntil(() => vm.DatabaseState is not (DatabaseState.Checking or DatabaseState.NotConfigured));

        Assert.AreEqual(DatabaseState.Offline, vm.DatabaseState);
        Assert.IsTrue(vm.IsMediaScannerAvailable);
        Assert.IsFalse(vm.ScanMediaCommand.CanExecute(null));
        StringAssert.Contains(vm.ScanMediaToolTip, "needs the database");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(50);
    }
}
