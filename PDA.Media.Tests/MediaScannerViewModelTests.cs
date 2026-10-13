using System.IO;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

/// <summary>The media scanner window's view model, against temp folders and a real SQL Server database (Inconclusive without one).</summary>
[TestClass]
public sealed class MediaScannerViewModelTests
{
    [TestMethod]
    public async Task TestCategoriesLoadWithTheActiveOnesTicked()
    {
        using var setup = new Setup();
        setup.AddCategory("Music", "Music");
        MediaCategory old = setup.AddCategory("Old Movies", "Movie");
        old.IsActive = false;
        new MediaCategoryService(setup.Db.ContextFactory).UpdateRecord(old.Id, old);
        setup.AddCategory("Books", "Audiobooks");
        var vm = setup.ViewModel();

        await vm.LoadCategoriesCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "Books", "Music", "Old Movies" }, vm.Categories.Select(c => c.Name).ToList());
        Assert.IsFalse(Category(vm, "Books").CanScan);
        Assert.IsFalse(Category(vm, "Books").IsSelected, "it can't be scanned");
        StringAssert.Contains(Category(vm, "Books").LastScanText, "Can't be scanned");
        Assert.IsTrue(Category(vm, "Music").IsSelected);
        Assert.AreEqual("Never scanned", Category(vm, "Music").LastScanText);
        Assert.IsFalse(Category(vm, "Old Movies").IsSelected, "inactive");
        StringAssert.EndsWith(Category(vm, "Old Movies").DetailText, "(inactive)");

        vm.SelectAllCommand.Execute(null);
        Assert.IsTrue(Category(vm, "Old Movies").IsSelected);
        Assert.IsFalse(Category(vm, "Books").IsSelected);
        vm.SelectNoneCommand.Execute(null);
        Assert.IsFalse(vm.StartScanCommand.CanExecute(null), "nothing ticked");
    }

    [TestMethod]
    public async Task TestScanShowsProgressAndResultsAndSavesTheOptions()
    {
        using var setup = new Setup();
        setup.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        setup.File("Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        setup.AddCategory("Music", "Music");
        int movies = setup.AddCategory("Movies", "Movie").Id;
        var vm = setup.ViewModel();
        await vm.LoadCategoriesCommand.ExecuteAsync(null);
        Category(vm, "Music").IsSelected = false;
        vm.MaxDeletePercent = 40;
        string? finished = null;
        vm.ScanFinished += summary => finished = summary;

        await vm.StartScanCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsScanning);
        Assert.AreEqual("Scan finished: 1 added, 0 restored, 0 updated, 0 deleted", finished);
        Assert.AreEqual(finished, vm.StatusText);
        Assert.AreEqual(("Finished", 1.0, 1.0), (vm.ProgressText, vm.ProgressValue, vm.ProgressMaximum));
        Assert.AreEqual("Movies", vm.Results.Single().CategoryName);
        Assert.IsTrue(vm.HasResults);
        Assert.IsTrue(vm.ActivityEntries.Any(e => e.Text == "Added movie Alien (1979)" && e.IsAdded && e.Folder!.EndsWith("Alien (1979)")));
        Assert.IsTrue(vm.ActivityEntries.Any(e => e.Text.StartsWith("Scan finished")));
        StringAssert.StartsWith(Category(vm, "Movies").LastScanText, "Last scan ");
        Assert.AreEqual("Never scanned", Category(vm, "Music").LastScanText);
        Assert.IsEmpty(new TrackService(setup.Db.ContextFactory).GetAllRecords(), "Music wasn't ticked");

        UserSettings settings = setup.Temp.SettingsService.LoadSettings();
        CollectionAssert.AreEqual(new[] { movies }, settings.MediaScanCategoryIds);
        Assert.AreEqual((false, true, 40), (settings.MediaScanDryRun, settings.MediaScanLimitDeletions, settings.MediaScanMaxDeletePercent));

        // A new window ticks the same categories and keeps the options.
        var again = setup.ViewModel();
        await again.LoadCategoriesCommand.ExecuteAsync(null);
        Assert.IsFalse(Category(again, "Music").IsSelected);
        Assert.IsTrue(Category(again, "Movies").IsSelected);
        Assert.AreEqual(40m, again.MaxDeletePercent);
    }

    [TestMethod]
    public async Task TestDryRun()
    {
        using var setup = new Setup();
        setup.File("Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        setup.AddCategory("Movies", "Movie");
        var vm = setup.ViewModel();
        await vm.LoadCategoriesCommand.ExecuteAsync(null);
        vm.DryRun = true;

        await vm.StartScanCommand.ExecuteAsync(null);

        Assert.AreEqual("Dry run finished (nothing saved): 1 to add, 0 to restore, 0 to update, 0 to delete", vm.StatusText);
        Assert.IsTrue(vm.ActivityEntries.Any(e => e.Text == "(dry run) Added movie Alien (1979)"));
        Assert.IsNull(new MovieService(setup.Db.ContextFactory).GetRecordByName("Alien (1979)"));
        Assert.AreEqual("Never scanned", Category(vm, "Movies").LastScanText);
    }

    [TestMethod]
    public async Task TestClearDataNeedsEveryActiveCategoryAndAsksFirst()
    {
        using var setup = new Setup();
        setup.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        Directory.CreateDirectory(setup.Path("Movies"));
        setup.AddCategory("Music", "Music");
        setup.AddCategory("Movies", "Movie");
        var vm = setup.ViewModel();
        await vm.LoadCategoriesCommand.ExecuteAsync(null);
        await vm.StartScanCommand.ExecuteAsync(null);
        var albums = new AlbumService(setup.Db.ContextFactory);
        int goldId = albums.GetAllRecords().Single().Id;

        Assert.IsTrue(vm.CanClearData);
        vm.ClearData = true;
        Category(vm, "Movies").IsSelected = false;
        Assert.IsFalse(vm.CanClearData);
        Assert.IsFalse(vm.ClearData, "unticked when it isn't allowed any more");
        StringAssert.Contains(vm.ClearDataToolTip, "tick every active category");
        Category(vm, "Movies").IsSelected = true;
        vm.DryRun = true;
        Assert.IsFalse(vm.CanClearData);
        vm.DryRun = false;

        // Start asks first; Back doesn't scan.
        vm.ClearData = true;
        await vm.StartScanCommand.ExecuteAsync(null);
        Assert.AreEqual("Clear all media data?", vm.Confirmation!.Title);
        Assert.IsFalse(vm.StartScanCommand.CanExecute(null));
        vm.DismissConfirmationCommand.Execute(null);
        Assert.IsFalse(vm.HasConfirmation);
        Assert.AreEqual(goldId, albums.GetAllRecords().Single().Id);

        // Confirm clears and scans everything again (new ids); the option is not kept for the next scan.
        await vm.StartScanCommand.ExecuteAsync(null);
        vm.ConfirmCommand.Execute(null);
        await vm.ScanCommand.ExecutionTask!;
        Assert.AreNotEqual(goldId, albums.GetAllRecords().Single().Id);
        Assert.IsTrue(vm.ActivityEntries.Any(e => e.Text == "Cleared the media cache"));
        Assert.IsFalse(vm.ClearData);
    }

    [TestMethod]
    public async Task TestClosingDuringAScanAsksAndCancels()
    {
        using var setup = new Setup();
        setup.File("Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        setup.AddCategory("Movies", "Movie");
        var vm = setup.ViewModel();
        await vm.LoadCategoriesCommand.ExecuteAsync(null);
        bool closeRequested = false;
        vm.CloseRequested += () => closeRequested = true;
        Assert.IsTrue(vm.CanClose(), "nothing running");

        Task scan = vm.ScanCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsScanning);
        Assert.IsFalse(vm.CanClose());
        Assert.AreEqual("Cancel scan and close", vm.Confirmation!.ConfirmText);
        vm.ConfirmCommand.Execute(null);
        await scan;

        Assert.IsTrue(closeRequested, "once the scan has stopped (cancelled, or finished first)");
        Assert.IsFalse(vm.IsScanning);
    }

    [TestMethod]
    public async Task TestSaveActivityLog()
    {
        using var setup = new Setup();
        setup.File("Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        setup.AddCategory("Movies", "Movie");
        var vm = setup.ViewModel();
        await vm.LoadCategoriesCommand.ExecuteAsync(null);
        await vm.StartScanCommand.ExecuteAsync(null);
        Directory.CreateDirectory(setup.Path("Downloads"));

        string path = vm.SaveActivityLog(setup.Path("Downloads"));

        string text = File.ReadAllText(path);
        StringAssert.Contains(text, "[Information] Added movie Alien (1979) (");
        vm.ClearActivityCommand.Execute(null);
        Assert.IsEmpty(vm.ActivityEntries);
    }

    private static MediaScanCategoryItem Category(MediaScannerViewModel vm, string name) => vm.Categories.Single(c => c.Name == name);

    /// <summary>A test database, a temp media folder and temp settings.</summary>
    private sealed class Setup : IDisposable
    {
        public SqlServerTestDatabase Db { get; } = new();
        public TempServices Temp { get; } = new();

        public string Path(params string[] parts) => System.IO.Path.Combine([Temp.SourceFolder, .. parts]);

        public void File(params string[] parts)
        {
            string path = Path(parts);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, [0]);
        }

        public MediaCategory AddCategory(string name, string mediaClass) =>
            new MediaCategoryService(Db.ContextFactory).AddRecord(new MediaCategory { Name = name, MediaClass = mediaClass, RootFolder = Path(name) });

        public MediaScannerViewModel ViewModel()
        {
            var f = Db.ContextFactory;
            var scanner = new MediaScanner(new MediaCategoryService(f), new ArtistService(f), new AlbumService(f), new AlbumTypeService(f), new TrackService(f),
                new MovieService(f), new TVShowService(f), new TVShowEpisodeService(f), new MediaCacheService(f), new SettingService(f));
            return new MediaScannerViewModel(scanner, Temp.SettingsService);
        }

        public void Dispose()
        {
            Temp.Dispose();
            Db.Dispose();
        }
    }
}
