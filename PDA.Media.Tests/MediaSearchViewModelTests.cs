using System.IO;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

/// <summary>The Media Library Services window's Search tab (and the window's closing rules), against temp folders scanned into SqlServerTestDatabase.</summary>
[TestClass]
public sealed class MediaSearchViewModelTests
{
    [TestMethod]
    public async Task TestCategoriesAndEmptySearch()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel vm = setup.Search();

        await vm.LoadCategoriesCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { "Movies", "Music", "TV" }, vm.Categories.Select(c => c.Name).ToArray(), "Books can't be scanned, so isn't offered");
        Assert.IsTrue(vm.Categories.All(c => c.IsSelected));

        await vm.SearchCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.StatusText, "Type something");
        Assert.IsEmpty(vm.Results);
    }

    [TestMethod]
    public async Task TestCategoryGroups()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel vm = await setup.LoadedSearchAsync();

        CollectionAssert.AreEqual(new[] { "Music", "Video" }, vm.CategoryGroups.Select(g => g.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Movies", "TV" }, vm.CategoryGroups[1].Categories.Select(c => c.Name).ToArray());
        SearchCategoryGroup video = vm.CategoryGroups[1];
        Assert.IsTrue(video.IsSelected);

        video.IsSelected = false;
        Assert.IsFalse(vm.Categories.Single(c => c.Name == "Movies").IsSelected);
        Assert.IsFalse(vm.Categories.Single(c => c.Name == "TV").IsSelected);
        Assert.IsTrue(vm.CategoryGroups[0].IsSelected, "the other group is left alone");

        var changes = new List<string?>();
        video.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        vm.Categories.Single(c => c.Name == "TV").IsSelected = true;
        Assert.IsNull(video.IsSelected, "some ticked");
        CollectionAssert.Contains(changes, nameof(SearchCategoryGroup.IsSelected));

        // Clicking a group that is in between ticks everything in it (a two-state tick box asks for false then).
        video.IsSelected = false;
        Assert.IsTrue(video.IsSelected);
        Assert.IsTrue(vm.Categories.Single(c => c.Name == "Movies").IsSelected);
    }

    [TestMethod]
    public async Task TestMovieCollectionHasItsOwnBranch()
    {
        using var setup = await Setup.CreateAsync();
        setup.File("Movies", "Compressed Blu-Rays", "Star Trek", "Star Trek (2009).m4v");
        setup.File("Movies", "Compressed Blu-Rays", "Star Trek", "Star Trek - Generations (1994).m4v");
        await setup.Scanner().ScanAllMediaCategoriesAsync();
        MediaSearchViewModel vm = await setup.LoadedSearchAsync();

        vm.SearchText = "star trek";
        await vm.SearchCommand.ExecuteAsync(null);

        MediaTreeNode source = vm.Results.Single().Children.Single();
        Assert.AreEqual("Compressed Blu-Rays", source.Title);
        MediaTreeNode collection = source.Children.Single();
        Assert.AreEqual((MediaNodeKind.Collection, "Star Trek", "2 movies"), (collection.Kind, collection.Title, collection.Subtitle));
        CollectionAssert.AreEquivalent(new[] { "Star Trek (2009)", "Star Trek - Generations (1994)" }, collection.Children.Select(c => c.Title).ToArray());

        vm.SearchText = "*";
        await vm.SearchCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { "Star Trek", "Heat (1995)" }, vm.Results[1].Children[0].Children.Select(c => c.Title).ToArray(),
            "the collection first, then the movies with folders of their own");
    }

    [TestMethod]
    public async Task TestSearchOpensTheWayToTheMatches()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel vm = await setup.LoadedSearchAsync();

        vm.SearchText = "queen";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.AreEqual("Found 2 artists", vm.StatusText);
        MediaTreeNode music = vm.Results.Single();
        Assert.AreEqual(("Music", "2 artists"), (music.Title, music.Subtitle));
        MediaTreeNode abba = music.Children.Single(n => n.Title == "ABBA");
        Assert.IsFalse(abba.IsMatch);
        Assert.IsTrue(abba.IsExpanded, "opened to the matching track");
        MediaTreeNode gold = abba.Children.Single();
        Assert.IsTrue(gold.IsExpanded);
        CollectionAssert.AreEqual(new[] { "01  Dancing Queen", "02  Waterloo" }, gold.Children.Select(n => n.Title).ToArray());
        Assert.IsTrue(gold.Children[0].IsMatch);
        Assert.IsFalse(gold.Children[1].IsMatch);
        Assert.IsTrue(music.Children.Single(n => n.Title == "Queen").IsMatch);

        // A branch that wasn't opened loads when it is.
        MediaTreeNode queen = music.Children.Single(n => n.Title == "Queen");
        Assert.IsFalse(queen.AreChildrenLoaded);
        Assert.AreEqual("Loading...", queen.Children.Single().Title);
        queen.IsExpanded = true;
        await queen.LoadChildrenAsync();
        Assert.AreEqual("A Night at the Opera", queen.Children.Single().Title);
    }

    [TestMethod]
    public async Task TestMoviesBySourceAndEpisodesBySeason()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel vm = await setup.LoadedSearchAsync();

        vm.SearchText = "*";
        await vm.SearchCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "Music", "Movies", "TV shows" }, vm.Results.Select(n => n.Title).ToArray());
        MediaTreeNode movies = vm.Results[1];
        CollectionAssert.AreEqual(new[] { "Compressed Blu-Rays", "Raw Blu-Rays" }, movies.Children.Select(n => n.Title).ToArray());
        Assert.AreEqual("Heat (1995)", movies.Children[0].Children.Single().Title);

        vm.SearchText = "serenity";
        await vm.SearchCommand.ExecuteAsync(null);
        MediaTreeNode firefly = vm.Results.Single().Children.Single();
        Assert.AreEqual("Firefly (2002)", firefly.Title);
        Assert.IsTrue(firefly.IsExpanded);
        MediaTreeNode season = firefly.Children.Single();
        Assert.AreEqual(("Season 1", "2 episodes · missing 2", true), (season.Title, season.Subtitle, season.IsExpanded));
        Assert.IsTrue(season.Children.Single(e => e.Title == "Serenity").IsMatch);
        Assert.AreEqual("s01e03", season.Children.Single(e => e.Title == "Our Mrs. Reynolds").Subtitle);
    }

    [TestMethod]
    public async Task TestDetailsAndSaveChanges()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel vm = await setup.LoadedSearchAsync();
        vm.SearchText = "gold";
        await vm.SearchCommand.ExecuteAsync(null);
        MediaTreeNode gold = vm.Results.Single().Children.Single().Children.Single();
        int live = new AlbumTypeService(setup.Db.ContextFactory).AddRecord(new AlbumType { Name = "Live" }).Id;

        vm.SelectedNode = gold;

        Assert.AreEqual(("Gold", "Album · ABBA"), (vm.DetailsTitle, vm.DetailsSubtitle));
        Assert.AreEqual("2", vm.DetailRows.Single(r => r.Label == "Tracks").Value);
        Assert.AreEqual(setup.Path("Music", "ABBA", "Gold"), vm.SelectedPath);
        MediaItemEditor editor = vm.Editor!;
        Assert.IsTrue(editor.HasType && editor.HasMusicBrainzId && editor.HasLength && !editor.HasReleaseDate);
        Assert.IsFalse(vm.SaveChangesCommand.CanExecute(null));

        editor.Notes = "Signed copy";
        editor.IsFavourite = true;
        Assert.IsTrue(editor.HasChanges);
        Assert.IsTrue(vm.SaveChangesCommand.CanExecute(null));
        editor.Notes = "";
        editor.IsFavourite = false;
        Assert.IsFalse(editor.HasChanges, "back as stored (empty text is no text)");

        editor.Notes = "Signed copy";
        editor.IsFavourite = true;
        await vm.SaveChangesCommand.ExecuteAsync(null);

        Album stored = new AlbumService(setup.Db.ContextFactory).GetRecordById(gold.Entity!.Id)!;
        Assert.AreEqual(("Signed copy", true, "Gold"), (stored.Notes, stored.IsFavourite, stored.Name));
        Assert.IsTrue(gold.IsFavourite, "the tree shows the saved row");
        Assert.IsFalse(vm.Editor!.HasChanges);
        Assert.AreEqual("Saved Gold", vm.StatusText);
    }

    [TestMethod]
    public async Task TestUnsavedChangesAreAskedAbout()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel vm = await setup.LoadedSearchAsync();
        vm.SearchText = "*";
        await vm.SearchCommand.ExecuteAsync(null);
        MediaTreeNode heat = vm.Results[1].Children[0].Children.Single();
        MediaTreeNode alien = vm.Results[1].Children[1].Children.Single();
        vm.SelectedNode = heat;
        vm.Editor!.Description = "Michael Mann";

        // Selecting another row asks; Discard shows it without saving.
        vm.SelectedNode = alien;
        Assert.AreEqual("Save your changes?", vm.Confirmation!.Title);
        Assert.AreEqual("Heat (1995)", vm.DetailsTitle, "still showing the changed row");
        vm.DismissConfirmationCommand.Execute(null);
        Assert.AreEqual("Alien", vm.DetailsTitle);
        Assert.IsNull(new MovieService(setup.Db.ContextFactory).GetRecordByName("Heat (1995)")!.Description);

        // Closing asks too; Save changes saves, then asks for the window to close.
        vm.SelectedNode = heat;
        vm.Editor!.Description = "Michael Mann";
        bool closeRequested = false;
        vm.CloseRequested += () => closeRequested = true;
        Assert.IsFalse(vm.CanClose());
        vm.ConfirmCommand.Execute(null);
        for (int i = 0; i < 100 && !closeRequested; i++) await Task.Delay(50);
        Assert.IsTrue(closeRequested);
        Assert.AreEqual("Michael Mann", new MovieService(setup.Db.ContextFactory).GetRecordByName("Heat (1995)")!.Description);
        Assert.IsTrue(vm.CanClose());
    }

    [TestMethod]
    public async Task TestWindowShowsTheTabThatAsks()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel search = await setup.LoadedSearchAsync();
        var library = new MediaLibraryViewModel(search, new MediaScannerViewModel(setup.Scanner(), setup.Temp.SettingsService)) { SelectedTabIndex = MediaLibraryViewModel.ScannerTab };
        Assert.AreEqual("", library.DatabaseText);
        Assert.IsTrue(library.CanClose(), "nothing to ask");

        search.SearchText = "gold";
        await search.SearchCommand.ExecuteAsync(null);
        search.SelectedNode = search.Results.Single().Children.Single().Children.Single();
        search.Editor!.Notes = "Changed";

        Assert.IsFalse(library.CanClose());
        Assert.AreEqual(MediaLibraryViewModel.SearchTab, library.SelectedTabIndex);
        Assert.IsTrue(search.HasConfirmation);
    }

    /// <summary>Music, movies and TV folders scanned into a test database, a category that can't be scanned, and temp settings.</summary>
    private sealed class Setup : IDisposable
    {
        public SqlServerTestDatabase Db { get; } = new();
        public TempServices Temp { get; } = new();

        public static async Task<Setup> CreateAsync()
        {
            var setup = new Setup();
            setup.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
            setup.File("Music", "ABBA", "Gold", "02 - Waterloo.flac");
            setup.File("Music", "Queen", "A Night at the Opera", "11 - Bohemian Rhapsody.flac");
            setup.File("Movies", "Compressed Blu-Rays", "Heat (1995)", "Heat (1995).mkv");
            setup.File("Movies", "Raw Blu-Rays", "Alien", "Alien.mkv");
            setup.File("TV", "Firefly (2002)", "Season 01", "Firefly (2002) - s01e01 - Serenity.mkv");
            setup.File("TV", "Firefly (2002)", "Season 01", "Firefly (2002) - s01e03 - Our Mrs. Reynolds.mkv");
            var categories = new MediaCategoryService(setup.Db.ContextFactory);
            foreach ((string name, string mediaClass) in new[] { ("Music", "Music"), ("Movies", "Movie"), ("TV", "TV"), ("Books", "Audiobooks") })
            {
                categories.AddRecord(new MediaCategory { Name = name, MediaClass = mediaClass, RootFolder = setup.Path(name) });
            }
            await setup.Scanner().ScanAllMediaCategoriesAsync();
            return setup;
        }

        public string Path(params string[] parts) => System.IO.Path.Combine([Temp.SourceFolder, .. parts]);

        public void File(params string[] parts)
        {
            string path = Path(parts);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, [0]);
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

        public async Task<MediaSearchViewModel> LoadedSearchAsync()
        {
            MediaSearchViewModel vm = Search();
            await vm.LoadCategoriesCommand.ExecuteAsync(null);
            return vm;
        }

        public void Dispose()
        {
            Temp.Dispose();
            Db.Dispose();
        }
    }
}
