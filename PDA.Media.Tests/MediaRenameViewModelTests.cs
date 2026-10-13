using System.IO;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

/// <summary>Rename and Tidy names in the Media Library's Search tab, against temp folders scanned into SqlServerTestDatabase.</summary>
[TestClass]
public sealed class MediaRenameViewModelTests
{
    [TestMethod]
    public async Task TestRenameFromTheSearchTab()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel vm = await setup.SearchAsync("matrix");
        MediaTreeNode matrix = vm.Results.Single().Children.Single().Children.Single();
        vm.SelectedNode = matrix;

        // Not with unsaved edits.
        vm.Editor!.Notes = "Changed";
        Assert.IsFalse(vm.RenameItemCommand.CanExecute(null));
        vm.DiscardChangesCommand.Execute(null);
        Assert.IsTrue(vm.RenameItemCommand.CanExecute(null));

        vm.RenameItemCommand.Execute(null);
        MediaRenameViewModel rename = vm.Rename!;
        Assert.AreEqual(("Rename movie", "The.Matrix.1999.REMUX"), (rename.Title, rename.CurrentName));
        Assert.IsTrue(rename.HasFolderOption && rename.HasFilesOption);
        rename.TidyCommand.Execute(null);
        Assert.AreEqual("The Matrix (1999)", rename.NewName);
        await rename.PreviewCommand.ExecuteAsync(null);
        Assert.HasCount(2, rename.Steps);
        Assert.IsTrue(rename.HasWarnings, "the Plex warning");
        Assert.IsTrue(rename.ApplyCommand.CanExecute(null));

        await rename.ApplyCommand.ExecuteAsync(null);
        for (int i = 0; i < 100 && matrix.Title != "The Matrix (1999)"; i++) await Task.Delay(50);

        Assert.IsNull(vm.Rename, "closed");
        Assert.AreEqual("The Matrix (1999)", matrix.Title);
        Assert.AreEqual("Renamed The.Matrix.1999.REMUX to The Matrix (1999)", vm.StatusText);
        Assert.IsTrue(Directory.Exists(setup.Path("Movies", "Raw Blu-Rays", "The Matrix (1999)")));
        Assert.AreEqual(matrix.Entity!.Id, new MovieService(setup.Db.ContextFactory).GetRecordByName("The Matrix (1999)")!.Id);
    }

    [TestMethod]
    public async Task TestRenameProblemsAndFailures()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel vm = await setup.SearchAsync("matrix");
        vm.SelectedNode = vm.Results.Single().Children.Single().Children.Single();
        vm.RenameItemCommand.Execute(null);
        MediaRenameViewModel rename = vm.Rename!;

        rename.NewName = "The Matrix: Reloaded";
        await rename.PreviewCommand.ExecuteAsync(null);
        Assert.IsTrue(rename.HasProblems);
        Assert.IsFalse(rename.ApplyCommand.CanExecute(null));

        // The folder appears after the preview: Apply fails, nothing changes, and the reason is shown.
        rename.NewName = "The Matrix (1999)";
        await rename.PreviewCommand.ExecuteAsync(null);
        Directory.CreateDirectory(setup.Path("Movies", "Raw Blu-Rays", "The Matrix (1999)"));
        await rename.ApplyCommand.ExecuteAsync(null);
        StringAssert.Contains(rename.ResultText, "already exists");
        Assert.IsNotNull(vm.Rename, "still open");
        Assert.IsTrue(File.Exists(setup.Path("Movies", "Raw Blu-Rays", "The.Matrix.1999.REMUX", "The.Matrix.1999.REMUX.mkv")));

        rename.CloseCommand.Execute(null);
        Assert.IsNull(vm.Rename);
    }

    [TestMethod]
    public async Task TestTidyNamesInABranch()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel vm = await setup.SearchAsync("*");
        MediaTreeNode movies = vm.Results.Single(n => n.Title == "Movies");
        vm.SelectedNode = movies;

        vm.TidyNamesInBranchCommand.Execute(null);
        TidyNamesViewModel tidy = vm.TidyNames!;
        await tidy.LoadCommand.ExecutionTask!; // started when the panel opened

        // Heat (1995) is already tidy; the scene-named Heat would clash with it, so it is unticked with the reason.
        CollectionAssert.AreEquivalent(new[] { "Heat.1995.1080p.BluRay", "The.Matrix.1999.REMUX" }, tidy.Rows.Select(r => r.CurrentName).ToArray());
        TidyRow clash = tidy.Rows.Single(r => r.CurrentName.StartsWith("Heat"));
        Assert.AreEqual((TidyRowStatus.Problem, false), (clash.Status, clash.IsSelected));
        StringAssert.Contains(clash.Message, "already called Heat (1995)");
        TidyRow matrix = tidy.Rows.Single(r => r.CurrentName.StartsWith("The.Matrix"));
        Assert.AreEqual(("The Matrix (1999)", true), (matrix.NewName, matrix.IsSelected));

        // An edited suggestion is used; the ticked rows are renamed.
        clash.NewName = "Heat (1995) - 4K";
        clash.IsSelected = true;
        await tidy.ApplyCommand.ExecuteAsync(null);

        Assert.AreEqual((TidyRowStatus.Done, TidyRowStatus.Done), (clash.Status, matrix.Status), clash.Message + " / " + matrix.Message);
        Assert.AreEqual("2 renamed", tidy.StatusText);
        Assert.IsTrue(Directory.Exists(setup.Path("Movies", "Raw Blu-Rays", "Heat (1995) - 4K")));

        tidy.CloseCommand.Execute(null);
        Assert.IsNull(vm.TidyNames);
        for (int i = 0; i < 100 && vm.IsSearching; i++) await Task.Delay(50);
        await Task.Delay(100);
        Assert.IsTrue(vm.Results.Single(n => n.Title == "Movies").Children.SelectMany(s => s.Children).Any(m => m.Title == "The Matrix (1999)"),
            "the search runs again after renames");
    }

    /// <summary>Movies (one already tidy, two scene names) scanned into a test database, and a search view model with renaming.</summary>
    private sealed class Setup : IDisposable
    {
        public SqlServerTestDatabase Db { get; } = new();
        public TempServices Temp { get; } = new();

        public static async Task<Setup> CreateAsync()
        {
            var setup = new Setup();
            setup.File("Movies", "Compressed Blu-Rays", "Heat (1995)", "Heat (1995).mkv");
            setup.File("Movies", "Raw Blu-Rays", "Heat.1995.1080p.BluRay", "Heat.1995.1080p.BluRay.mkv");
            setup.File("Movies", "Raw Blu-Rays", "The.Matrix.1999.REMUX", "The.Matrix.1999.REMUX.mkv");
            new MediaCategoryService(setup.Db.ContextFactory).AddRecord(new MediaCategory { Name = "Movies", MediaClass = "Movie", RootFolder = setup.Path("Movies") });
            await setup.Scanner.ScanAllMediaCategoriesAsync();
            return setup;
        }

        private Setup()
        {
            var f = Db.ContextFactory;
            Scanner = new MediaScanner(new MediaCategoryService(f), new ArtistService(f), new AlbumService(f), new AlbumTypeService(f), new TrackService(f),
                new MovieService(f), new TVShowService(f), new TVShowEpisodeService(f), new MediaCacheService(f), new SettingService(f));
        }

        public MediaScanner Scanner { get; }

        public string Path(params string[] parts) => System.IO.Path.Combine([Temp.SourceFolder, .. parts]);

        private void File(params string[] parts)
        {
            string path = Path(parts);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, [0]);
        }

        public async Task<MediaSearchViewModel> SearchAsync(string text)
        {
            var f = Db.ContextFactory;
            var renamer = new MediaRenameService(Scanner, new MediaPathService(f), new ArtistService(f), new AlbumService(f), new TrackService(f), new MovieService(f),
                new TVShowService(f), new TVShowEpisodeService(f));
            var vm = new MediaSearchViewModel(Scanner, new MediaSearchService(f), new AlbumTypeService(f), new MovieTypeService(f), new TVShowTypeService(f), null, renamer);
            await vm.LoadCategoriesCommand.ExecuteAsync(null);
            vm.SearchText = text;
            await vm.SearchCommand.ExecuteAsync(null);
            return vm;
        }

        public void Dispose()
        {
            Temp.Dispose();
            Db.Dispose();
        }
    }
}
