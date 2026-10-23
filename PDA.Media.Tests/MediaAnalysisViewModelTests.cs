using System.IO;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

/// <summary>The Media Library Services window's Analysis tab, against temp folders scanned into SqlServerTestDatabase (Inconclusive without one).</summary>
[TestClass]
public sealed class MediaAnalysisViewModelTests
{
    [TestMethod]
    public async Task TestSummaryAndTree()
    {
        using var setup = await Setup.CreateAsync();
        MediaAnalysisViewModel vm = await setup.LoadedAnalysisAsync();

        CollectionAssert.AreEqual(new[] { "Library", "Movies", "Music", "TV" }, vm.Summaries.Select(s => s.Name).ToArray(), "Books can't be scanned");
        Assert.AreEqual("8 files in 3 categories", vm.Summaries[0].Counts);
        Assert.AreEqual("3 tracks · 2 albums · 2 artists", vm.Summaries[2].Counts);
        Assert.AreEqual("2 movies · 2 source folders", vm.Summaries[1].Counts);
        Assert.AreEqual("3 episodes · 1 season · 1 show", vm.Summaries[3].Counts);
        StringAssert.StartsWith(vm.Summaries[2].LastScan, "Last scan ");
        Assert.IsNull(vm.Summaries[2].Warning);

        AnalysisTreeNode music = vm.Roots.Single(r => r.Title == "Music");
        Assert.AreEqual(("3", "2", "4 KB"), (music.ItemsText, music.FoldersText, music.SizeText));
        music.IsExpanded = true;
        await music.LoadChildrenAsync();
        CollectionAssert.AreEqual(new[] { "ABBA", "Queen" }, music.Children.Select(c => c.Title).ToArray());
        Assert.AreEqual(("2", "3 KB", "67%"), (music.Children[0].ItemsText, music.Children[0].SizeText, music.Children[0].ShareText));

        vm.SortBySize = true;
        CollectionAssert.AreEqual(new[] { "ABBA", "Queen" }, music.Children.Select(c => c.Title).ToArray());
        AnalysisTreeNode abba = music.Children[0];
        await abba.LoadChildrenAsync();
        await abba.Children.Single().LoadChildrenAsync();
        CollectionAssert.AreEqual(new[] { "02 - Waterloo", "01 - Dancing Queen" }, abba.Children.Single().Children.Select(c => c.Title).ToArray(),
            "largest first, as the branch is read");
        vm.SortBySize = false;
        CollectionAssert.AreEqual(new[] { "01 - Dancing Queen", "02 - Waterloo" }, abba.Children.Single().Children.Select(c => c.Title).ToArray(), "back to track order");
    }

    [TestMethod]
    public async Task TestDetailsLargestFileTypesAndChecks()
    {
        using var setup = await Setup.CreateAsync();
        MediaAnalysisViewModel vm = await setup.LoadedAnalysisAsync();
        AnalysisTreeNode music = vm.Roots.Single(r => r.Title == "Music");

        vm.SelectedNode = music;
        await vm.LoadDetailsAsync(music);

        Assert.AreEqual("Music", vm.DetailsTitle);
        Assert.AreEqual("3", vm.DetailRows.Single(r => r.Label == "Files").Value);
        CollectionAssert.AreEqual(new[] { "02 - Waterloo", "11 - Bohemian Rhapsody", "01 - Dancing Queen" }, vm.Largest.Select(l => l.Name).ToArray());
        Assert.AreEqual(("1.", 100.0, "ABBA · Gold"), (vm.Largest[0].RankText, vm.Largest[0].SharePercent, vm.Largest[0].Detail));
        Assert.AreEqual(50.0, vm.Largest[2].SharePercent, "of the largest");
        Assert.IsTrue(vm.CanRankGroups);
        Assert.AreEqual(".flac", vm.FileTypes.Single().Extension);
        Assert.AreEqual("100%", vm.FileTypes.Single().ShareText);
        Assert.AreEqual("Albums without cover art", vm.Checks[0].Title);
        Assert.AreEqual(("OK", true), (vm.Checks.Single(c => c.Title == "Files without a size").CountText, vm.Checks.Single(c => c.Title == "Files without a size").IsClear));
        Assert.AreEqual("1 check found something", vm.ChecksText, "two albums without cover art");

        vm.RankGroups = true;
        await WaitFor(() => vm.Largest.Count == 2);
        CollectionAssert.AreEqual(new[] { "Gold", "A Night at the Opera" }, vm.Largest.Select(l => l.Name).ToArray());
        Assert.AreEqual("ABBA · 2 files", vm.Largest[0].Detail);

        vm.ShowLargest = false;
        Assert.IsEmpty(vm.Largest);

        // A file has no largest list, and albums can't be ranked inside an album.
        AnalysisTreeNode track = await setup.OpenAsync(music, "ABBA", "Gold", "01 - Dancing Queen");
        vm.ShowLargest = true;
        vm.SelectedNode = track;
        await vm.LoadDetailsAsync(track);
        Assert.IsEmpty(vm.Largest);
        Assert.IsFalse(vm.CanRankGroups);
        Assert.AreEqual("Track", vm.DetailsSubtitle.Split(" · ")[0]);
        Assert.AreEqual("33.3% of Gold", vm.DetailRows.Single(r => r.Label == "Share").Value);
    }

    [TestMethod]
    public async Task TestIncludeDeletedReadsTheTreeAgain()
    {
        using var setup = await Setup.CreateAsync();
        MediaAnalysisViewModel vm = await setup.LoadedAnalysisAsync();
        var tracks = new TrackService(setup.Db.ContextFactory);
        tracks.DeleteRecord(tracks.GetAllRecords("Waterloo").Single().Id);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.AreEqual("2", vm.Roots.Single(r => r.Title == "Music").ItemsText);
        StringAssert.Contains(vm.Summaries.Single(s => s.Name == "Music").Warning, "1 deleted row");

        vm.IncludeDeleted = true;
        await WaitFor(() => vm.Roots.Count > 0 && vm.Roots.Single(r => r.Title == "Music").ItemsText == "3");
    }

    [TestMethod]
    public async Task TestShowInSearch()
    {
        using var setup = await Setup.CreateAsync();
        MediaAnalysisViewModel vm = await setup.LoadedAnalysisAsync();
        MediaSearchViewModel search = setup.Search();
        var library = new MediaLibraryViewModel(search, new MediaScannerViewModel(setup.Scanner(), setup.Temp.SettingsService), null, vm)
        {
            SelectedTabIndex = MediaLibraryViewModel.AnalysisTab,
        };
        AnalysisTreeNode music = vm.Roots.Single(r => r.Title == "Music");
        vm.SelectedNode = music;
        await vm.LoadDetailsAsync(music);
        Assert.IsFalse(vm.ShowNodeInSearchCommand.CanExecute(null), "a category isn't a row");

        vm.ShowInSearchCommand.Execute(vm.Largest[0]);
        await WaitFor(() => search.Results.Count > 0);

        Assert.AreEqual(MediaLibraryViewModel.SearchTab, library.SelectedTabIndex);
        Assert.AreEqual("Waterloo", search.SearchText, "without its track number");
        CollectionAssert.AreEqual(new[] { "Music" }, search.Categories.Where(c => c.IsSelected).Select(c => c.Name).ToArray());
        Assert.AreEqual("ABBA", search.Results.Single().Children.Single().Title);

        // A season is looked for by its show.
        AnalysisTreeNode season = await setup.OpenAsync(vm.Roots.Single(r => r.Title == "TV"), "Firefly (2002)", "Season 1");
        vm.SelectedNode = season;
        vm.ShowNodeInSearchCommand.Execute(null);
        await WaitFor(() => search.SearchText == "Firefly" && !search.IsSearching && search.Results.Count > 0);
        Assert.AreEqual("Firefly (2002)", search.Results.Single().Children.Single().Title);
    }

    [TestMethod]
    public async Task TestExportCsv()
    {
        using var setup = await Setup.CreateAsync();
        MediaAnalysisViewModel vm = await setup.LoadedAnalysisAsync();
        AnalysisTreeNode music = vm.Roots.Single(r => r.Title == "Music");
        vm.SelectedNode = music;
        string folder = setup.Path("Exports");
        Directory.CreateDirectory(folder);

        string? path = await vm.ExportAsync(folder);

        Assert.IsNotNull(path);
        StringAssert.Contains(Path.GetFileName(path), "PDA.Media analysis - Music - ");
        string[] lines = File.ReadAllLines(path);
        Assert.AreEqual("Name,Belongs to,Folder,File,Size (bytes),Size,Modified,Deleted", lines[0].TrimStart('﻿'));
        Assert.HasCount(4, lines);
        StringAssert.StartsWith(lines[1], "01 - Dancing Queen,ABBA · Gold,");
        StringAssert.Contains(lines[1], ",01 - Dancing Queen.flac,1000,1000 B,");
        Assert.AreEqual("\"Waterloo, live\",\"say \"\"hi\"\"\",,,,,,", MediaAnalysisViewModel.ToCsv(
            [new MediaAnalysisItem(new MediaAnalysisScope(MediaAnalysisKind.Music, "x"), "Waterloo, live", "say \"hi\"", null, null, null, null, 1, false)]).Split(Environment.NewLine)[1]);
    }

    [TestMethod]
    public async Task TestDiskCheckAndStale()
    {
        using var setup = await Setup.CreateAsync();
        MediaAnalysisViewModel vm = await setup.LoadedAnalysisAsync();
        File.WriteAllBytes(setup.Path("Movies", "Raw Blu-Rays", "Up (2009).mkv"), new byte[10]);
        AnalysisTreeNode movies = vm.Roots.Single(r => r.Title == "Movies");
        vm.SelectedNode = movies;
        Assert.IsTrue(vm.CheckDiskCommand.CanExecute(null));

        await vm.CheckDiskCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HasDiskResult);
        Assert.AreEqual("1 not stored", vm.DiskStatusText);
        DiskIssueRow issue = vm.DiskIssues.Single();
        Assert.AreEqual(("Not stored", Path.Combine("Raw Blu-Rays", "Up (2009).mkv")), (issue.KindText, issue.RelativePath));
        Assert.AreEqual("2 files, 2 KB", vm.DiskRows.Single(r => r.Label == "In the database").Value);

        vm.SelectedNode = vm.Roots.Single(r => r.Title == "Music");
        Assert.IsFalse(vm.HasDiskResult, "a check belongs to the node it was run on");

        vm.MarkStale();
        Assert.IsTrue(vm.IsStale);
        StringAssert.Contains(vm.StatusText, "Refresh");
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.IsStale);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.IsTrue(condition(), "timed out");
    }

    /// <summary>Music, movies and TV folders (files of 1000 bytes; Waterloo 2000, Bohemian Rhapsody 1500) scanned into a test database, a category that can't be scanned, and temp settings.</summary>
    private sealed class Setup : IDisposable
    {
        public SqlServerTestDatabase Db { get; } = new();
        public TempServices Temp { get; } = new();

        public static async Task<Setup> CreateAsync()
        {
            var setup = new Setup();
            setup.File(1000, "Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
            setup.File(2000, "Music", "ABBA", "Gold", "02 - Waterloo.flac");
            setup.File(1500, "Music", "Queen", "A Night at the Opera", "11 - Bohemian Rhapsody.flac");
            setup.File(1000, "Movies", "Compressed Blu-Rays", "Heat (1995)", "Heat (1995).mkv");
            setup.File(1000, "Movies", "Raw Blu-Rays", "Alien", "Alien.mkv");
            setup.File(1000, "TV", "Firefly (2002)", "Season 01", "Firefly (2002) - s01e01 - Serenity.mkv");
            setup.File(1000, "TV", "Firefly (2002)", "Season 01", "Firefly (2002) - s01e02 - The Train Job.mkv");
            setup.File(1000, "TV", "Firefly (2002)", "Season 01", "Firefly (2002) - s01e03 - Our Mrs. Reynolds.mkv");
            var categories = new MediaCategoryService(setup.Db.ContextFactory);
            foreach ((string name, string mediaClass) in new[] { ("Music", "Music"), ("Movies", "Movie"), ("TV", "TV"), ("Books", "Audiobooks") })
            {
                categories.AddRecord(new MediaCategory { Name = name, MediaClass = mediaClass, RootFolder = setup.Path(name) });
            }
            await setup.Scanner().ScanAllMediaCategoriesAsync();
            return setup;
        }

        public string Path(params string[] parts) => System.IO.Path.Combine([Temp.SourceFolder, .. parts]);

        private void File(int size, params string[] parts)
        {
            string path = Path(parts);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, new byte[size]);
        }

        public MediaScanner Scanner()
        {
            var f = Db.ContextFactory;
            return new MediaScanner(new MediaCategoryService(f), new ArtistService(f), new AlbumService(f), new AlbumTypeService(f), new TrackService(f),
                new MovieService(f), new TVShowService(f), new TVShowEpisodeService(f), new MediaCacheService(f), new SettingService(f));
        }

        public MediaSearchViewModel Search()
        {
            var f = Db.ContextFactory;
            return new MediaSearchViewModel(Scanner(), new MediaSearchService(f), new AlbumTypeService(f), new MovieTypeService(f), new TVShowTypeService(f));
        }

        public async Task<MediaAnalysisViewModel> LoadedAnalysisAsync()
        {
            var analysis = new MediaAnalysisService(Db.ContextFactory);
            var vm = new MediaAnalysisViewModel(Scanner(), analysis, new MediaDiskCheckService(analysis));
            await vm.LoadCommand.ExecuteAsync(null);
            return vm;
        }

        /// <summary>Opens the branches from <paramref name="node"/> down the given titles and returns the last.</summary>
        public async Task<AnalysisTreeNode> OpenAsync(AnalysisTreeNode node, params string[] titles)
        {
            foreach (string title in titles)
            {
                await node.LoadChildrenAsync();
                node = node.Children.Single(c => c.Title == title);
            }
            return node;
        }

        public void Dispose()
        {
            Temp.Dispose();
            Db.Dispose();
        }
    }
}
