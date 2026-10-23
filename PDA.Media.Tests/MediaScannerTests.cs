using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils;
using PDA.Media.Utils.Logging;

namespace PDA.Media.Tests;

/// <summary>
/// MediaScanner against folder trees in a temp folder and a real SQL Server database (see SqlServerTestDatabase; Inconclusive without one).
/// </summary>
[TestClass]
public sealed class MediaScannerTests
{
    [TestMethod]
    public void TestAppContainerCreatesTheScanner()
    {
        // The app's own registrations: AddMediaData + AddMediaScanner. Nothing connects to a database until a service is used.
        using var services = ServiceConfiguration.BuildServiceProvider(new AuditLogSink(), "test.log");

        var scanner = services.GetRequiredService<MediaScanner>();

        Assert.AreSame(scanner, services.GetRequiredService<MediaScanner>(), "one scanner for the app (singleton)");
    }

    // Music.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestScanMusic(bool async)
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        disk.File("Music", "ABBA", "Gold", "02 - Knowing Me, Knowing You.flac");
        disk.File("Music", "ABBA", "Gold", "cover.jpg");
        disk.File("Music", "ABBA", "Gold", "notes.txt");
        disk.File("Music", "Björk", "Homogenic", "05 - Jóga.mp3");
        disk.File("Music", "Björk", "Empty Folder", "readme.txt");
        disk.File("Music", "@eaDir", "Thumbs", "01 - Not Music.flac");
        int compilation = new AlbumTypeService(db.ContextFactory).AddRecord(new AlbumType { Name = "Compilation" }).Id;
        AddCategory(db, "Music", "Music", disk.Path("Music"), compilation);

        MediaScanResult result = async ? await Scanner(db).ScanAllMediaCategoriesAsync() : Scanner(db).ScanAllMediaCategories();

        Assert.AreEqual(1, result.CategoriesScanned);
        Assert.AreEqual(2 + 2 + 3, result.Added, "2 artists, 2 albums, 3 tracks");
        Album gold = new AlbumService(db.ContextFactory).GetAllRecords("Gold").Single();
        Assert.AreEqual(2, gold.TrackCount);
        Assert.AreEqual("cover.jpg", gold.CoverArtFilename);
        Assert.AreEqual(compilation, gold.AlbumTypeId, "the category's album type");
        Assert.AreEqual(disk.Path("Music", "ABBA", "Gold"), gold.Folder);
        List<Track> tracks = new TrackService(db.ContextFactory).GetAllRecordsByAlbumId(gold.Id);
        CollectionAssert.AreEqual(new[] { "Dancing Queen", "Knowing Me, Knowing You" }, tracks.Select(t => t.Name).ToArray());
        Assert.AreEqual("01 - Dancing Queen.flac", tracks[0].OriginalFilename);
        Assert.AreEqual("Jóga", new TrackService(db.ContextFactory).GetAllRecords("Jóga").Single().Name);
        Assert.IsNull(new AlbumService(db.ContextFactory).GetAllRecords("Empty Folder").SingleOrDefault(), "album folders without audio are ignored");
        Assert.IsNull(new ArtistService(db.ContextFactory).GetRecordByName("@eaDir"), "NAS housekeeping folders are ignored");
    }

    [TestMethod]
    public void TestRescanIsIncremental()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        disk.File("Music", "ABBA", "Gold", "02 - Waterloo.flac");
        disk.File("Music", "ABBA", "Arrival", "01 - When I Kissed the Teacher.flac");
        AddCategory(db, "Music", "Music", disk.Path("Music"));
        Scanner(db).ScanAllMediaCategories();
        var albums = new AlbumService(db.ContextFactory);
        Album arrival = albums.GetAllRecords("Arrival").Single();
        arrival.IsFavourite = true;
        arrival.Notes = "Bought on vinyl";
        albums.UpdateRecord(arrival.Id, arrival);

        // Nothing changed on disk: nothing changes in the database.
        MediaScanResult unchanged = Scanner(db).ScanAllMediaCategories();
        Assert.AreEqual(0, unchanged.Added + unchanged.Updated + unchanged.Deleted + unchanged.Restored);
        Assert.AreEqual(1 + 2 + 3, unchanged.Unchanged);

        // An album folder goes: it and its track are soft-deleted; a track is added to another album.
        Directory.Move(disk.Path("Music", "ABBA", "Arrival"), disk.Path("Arrival (moved away)"));
        disk.File("Music", "ABBA", "Gold", "03 - Mamma Mia.flac");
        MediaScanResult changed = Scanner(db).ScanAllMediaCategories();
        Assert.AreEqual(1, changed.Added, "Mamma Mia");
        Assert.AreEqual(1, changed.Updated, "Gold's track count");
        Assert.AreEqual(2, changed.Deleted, "Arrival and its track");
        Assert.IsNull(albums.GetRecordById(arrival.Id));
        Assert.AreEqual(3, albums.GetAllRecords("Gold").Single().TrackCount);

        // It comes back: restored with its id, favourite and notes.
        Directory.Move(disk.Path("Arrival (moved away)"), disk.Path("Music", "ABBA", "Arrival"));
        MediaScanResult restored = Scanner(db).ScanAllMediaCategories();
        Assert.AreEqual(2, restored.Restored);
        Album back = albums.GetRecordById(arrival.Id)!;
        Assert.IsTrue(back.IsFavourite);
        Assert.AreEqual("Bought on vinyl", back.Notes);
    }

    [TestMethod]
    public void TestArtistInTwoMusicCategories()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Music", "Queen", "A Night at the Opera", "11 - Bohemian Rhapsody.flac");
        disk.File("Live", "Queen", "Live Killers", "01 - We Will Rock You.flac");
        AddCategory(db, "Live", "Music", disk.Path("Live"));
        AddCategory(db, "Music", "Music", disk.Path("Music"));
        Scanner(db).ScanAllMediaCategories();
        var artists = new ArtistService(db.ContextFactory);
        Artist queen = artists.GetRecordByName("Queen")!;
        Assert.AreEqual(disk.Path("Live", "Queen"), queen.Folder, "the first category scanned (by name) claims the artist");

        // Scanning again doesn't move the artist back and forth.
        MediaScanResult again = Scanner(db).ScanAllMediaCategories();
        Assert.AreEqual((0, 0), (again.Updated, again.Deleted));

        // Gone from Live: the artist stays (it still has an album in Music) and moves there.
        Directory.Delete(disk.Path("Live", "Queen"), true);
        MediaScanResult gone = Scanner(db).ScanAllMediaCategories();
        Assert.AreEqual(2, gone.Deleted, "Live Killers and its track, not the artist");
        Artist moved = artists.GetRecordById(queen.Id)!;
        Assert.AreEqual(disk.Path("Music", "Queen"), moved.Folder);

        // Gone from Music too: now the artist goes.
        Directory.Delete(disk.Path("Music", "Queen"), true);
        Scanner(db).ScanAllMediaCategories();
        Assert.IsNull(artists.GetRecordById(queen.Id));
    }

    // Movies and TV.
    // ==================================================================================================

    [TestMethod]
    public void TestScanMovies()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        // A movie in its own folder, with a sample and extras that aren't movies of their own.
        disk.File(3000, "Movies", "Compressed Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        disk.File(10, "Movies", "Compressed Blu-Rays", "Alien (1979)", "Alien (1979) - Sample.mkv");
        disk.File(20, "Movies", "Compressed Blu-Rays", "Alien (1979)", "Extras", "Making Of.mkv");
        disk.File("Movies", "Compressed Blu-Rays", "Alien (1979)", "poster.jpg");
        // Folder names don't need a year.
        disk.File("Movies", "Raw Blu-Rays", "千と千尋の神隠し", "Title_t00.mkv");
        // Loose movie files in a source folder.
        disk.File("Movies", "Compressed Movies", "Heat (1995).mkv");
        disk.File("Movies", "Compressed Movies", "Ronin.mp4");
        // A folder of movie folders, two levels below the source folder.
        disk.File("Movies", "Concert Movies", "Queen", "Live at Wembley (1986)", "Live at Wembley.mkv");
        disk.File("Movies", "Concert Movies", "Queen", "Live Aid (1985)", "Live Aid.mkv");
        // Folders without a video file anywhere, and an empty source folder.
        disk.File("Movies", "Filmous", "No Video", "info.nfo");
        Directory.CreateDirectory(disk.Path("Movies", "ARRstack"));
        AddCategory(db, "Movies", "Movie", disk.Path("Movies"));

        MediaScanResult result = Scanner(db).ScanAllMediaCategories();

        var movies = new MovieService(db.ContextFactory);
        CollectionAssert.AreEquivalent(new[] { "Alien (1979)", "千と千尋の神隠し", "Heat (1995)", "Ronin", "Live at Wembley (1986)", "Live Aid (1985)" },
            movies.GetAllRecords().Select(m => m.Name).ToList());
        Assert.AreEqual(6, result.Added);
        Movie alien = movies.GetRecordByName("Alien (1979)")!;
        Assert.AreEqual(disk.Path("Movies", "Compressed Blu-Rays", "Alien (1979)"), alien.Folder);
        Assert.AreEqual("Alien (1979).mkv", alien.OriginalFilename, "the largest video file, not the sample");
        Assert.AreEqual("poster.jpg", alien.CoverArtFilename);
        Assert.AreEqual("Title_t00.mkv", movies.GetRecordByName("千と千尋の神隠し")!.OriginalFilename);
        Movie heat = movies.GetRecordByName("Heat (1995)")!;
        Assert.AreEqual(disk.Path("Movies", "Compressed Movies"), heat.Folder);
        Assert.AreEqual("Heat (1995).mkv", heat.OriginalFilename);
        Assert.AreEqual(disk.Path("Movies", "Concert Movies", "Queen", "Live Aid (1985)"), movies.GetRecordByName("Live Aid (1985)")!.Folder);
    }

    [TestMethod]
    public void TestMovieMovedToAnotherSourceFolderIsUpdated()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        AddCategory(db, "Movies", "Movie", disk.Path("Movies"));
        Scanner(db).ScanAllMediaCategories();
        int id = new MovieService(db.ContextFactory).GetRecordByName("Alien (1979)")!.Id;

        // Compressed and moved: the same record now points at the new folder.
        Directory.Delete(disk.Path("Movies", "Raw Blu-Rays", "Alien (1979)"), true);
        disk.File("Movies", "Compressed Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        MediaScanResult result = Scanner(db).ScanAllMediaCategories();

        Assert.AreEqual((0, 1, 0), (result.Added, result.Updated, result.Deleted));
        Movie alien = new MovieService(db.ContextFactory).GetRecordById(id)!;
        Assert.AreEqual(disk.Path("Movies", "Compressed Blu-Rays", "Alien (1979)"), alien.Folder);
    }

    [TestMethod]
    public void TestDuplicateMovieIsReported()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Movies", "Compressed Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        disk.File("Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        AddCategory(db, "Movies", "Movie", disk.Path("Movies"));
        var logger = new CapturingLogger<MediaScanner>();

        MediaScanResult result = Scanner(db, logger).ScanAllMediaCategories();

        Assert.AreEqual(1, result.Added);
        Assert.AreEqual(disk.Path("Movies", "Compressed Blu-Rays", "Alien (1979)"), new MovieService(db.ContextFactory).GetRecordByName("Alien (1979)")!.Folder);
        Assert.IsTrue(logger.Messages.Any(m => m.Contains("Alien (1979)") && m.Contains("Raw Blu-Rays") && m.Contains("only the first is kept")),
            string.Join("\n", logger.Messages));
    }

    [TestMethod]
    public void TestScanTVShows()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("TV", "The Office (US) (2005)", "Season 01", "The Office (US) (2005) - s01e01 - Pilot.mkv");
        disk.File("TV", "The Office (US) (2005)", "Season 01", "The Office (US) (2005) - s01e02 - Diversity Day.mkv");
        disk.File("TV", "The Office (US) (2005)", "Specials", "The Office (US) (2005) - s00e01 - Webisode.mkv");
        disk.File("TV", "The Office (US) (2005)", "poster.jpg");
        disk.File("TV", "Firefly", "Season 1", "Firefly - s01e01 - Serenity.mkv");
        disk.File("TV", "New Show (2026)", "readme.txt");
        AddCategory(db, "TV", "TV Show", disk.Path("TV"));

        MediaScanResult result = Scanner(db).ScanAllMediaCategories();

        Assert.AreEqual(3 + 4, result.Added, "3 shows (one with no episodes yet), 4 episodes");
        var shows = new TVShowService(db.ContextFactory);
        TVShow office = shows.GetRecordByName("The Office (US)", 2005)!;
        Assert.AreEqual("poster.jpg", office.CoverArtFilename);
        Assert.IsNotNull(shows.GetRecordByName("Firefly", 0));
        List<TVShowEpisode> episodes = new TVShowEpisodeService(db.ContextFactory).GetAllRecordsByTVShowId(office.Id);
        CollectionAssert.AreEqual(new[] { "0x1 Webisode", "1x1 Pilot", "1x2 Diversity Day" },
            episodes.Select(e => $"{e.SeasonNo}x{e.EpisodeNo} {e.Name}").ToArray());
        Assert.AreEqual(disk.Path("TV", "The Office (US) (2005)", "Season 01"), episodes[1].Folder);
    }

    // Safety and options.
    // ==================================================================================================

    [TestMethod]
    public void TestUnreachableRootDeletesNothing()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        MediaCategory music = AddCategory(db, "Music", "Music", disk.Path("Music"));
        Scanner(db).ScanAllMediaCategories();

        // The NAS is off: the root folder can't be reached.
        Directory.Move(disk.Path("Music"), disk.Path("Music (offline)"));
        var logger = new CapturingLogger<MediaScanner>();
        MediaScanResult result = Scanner(db, logger).ScanAllMediaCategories();

        Assert.AreEqual(1, result.CategoriesSkipped);
        Assert.AreEqual(0, result.Deleted);
        Assert.AreEqual(3, db.Scalar<int>("SELECT COUNT(*) FROM Artists WHERE Id <> 0") + db.Scalar<int>("SELECT COUNT(*) FROM Albums WHERE IsDeleted = 0")
            + db.Scalar<int>("SELECT COUNT(*) FROM Tracks WHERE IsDeleted = 0"));
        Assert.IsTrue(logger.Messages.Any(m => m.Contains("can't be reached") && m.Contains(music.Name)), string.Join("\n", logger.Messages));
    }

    [TestMethod]
    public void TestUnknownMediaClassAndInactiveCategoriesAreSkipped()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Books", "Author", "Book", "01 - Chapter.mp3");
        AddCategory(db, "Books", "Audiobooks", disk.Path("Books"));
        new MediaCategoryService(db.ContextFactory).AddRecord(new MediaCategory { Name = "Old", MediaClass = "Music", RootFolder = disk.Path("Books"), IsActive = false });
        var logger = new CapturingLogger<MediaScanner>();

        MediaScanResult result = Scanner(db, logger).ScanAllMediaCategories();

        Assert.AreEqual(0, result.CategoriesScanned);
        Assert.AreEqual(1, result.CategoriesSkipped, "the inactive category isn't looked at");
        Assert.AreEqual(0, result.Added);
        Assert.IsTrue(logger.Messages.Any(m => m.Contains("isn't Music, Movie or TV")), string.Join("\n", logger.Messages));
    }

    [TestMethod]
    public void TestScanCanClearTheCacheFirst()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        AddCategory(db, "Music", "Music", disk.Path("Music"));
        Scanner(db).ScanAllMediaCategories();
        var albums = new AlbumService(db.ContextFactory);
        int firstId = albums.GetAllRecords().Single().Id;

        MediaScanResult result = Scanner(db).ScanAllMediaCategories(clearData: true);

        Assert.AreEqual(3, result.Added, "everything is added again");
        Assert.AreNotEqual(firstId, albums.GetAllRecords().Single().Id);
    }

    // Options: selected categories, progress, dry run, deletion limit, clearing, last scan.
    // ==================================================================================================

    [TestMethod]
    public void TestOnlyTheSelectedCategoriesAreScanned()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        disk.File("Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        AddCategory(db, "Music", "Music", disk.Path("Music"));
        MediaCategory movies = AddCategory(db, "Movies", "Movie", disk.Path("Movies"));
        movies.IsActive = false;
        new MediaCategoryService(db.ContextFactory).UpdateRecord(movies.Id, movies);

        MediaScanResult result = Scanner(db).ScanMediaCategories([movies.Id]);

        Assert.AreEqual(1, result.Added, "the movie only, though its category isn't active");
        Assert.AreEqual("Movies", result.Categories.Single().CategoryName);
        Assert.AreEqual(0, new ArtistService(db.ContextFactory).GetAllRecords().Count(a => a.Id != 0));
    }

    [TestMethod]
    public void TestProgressIsReported()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        disk.File("Music", "ABBA", "Gold", "02 - Waterloo.flac");
        disk.File("Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        int music = AddCategory(db, "Music", "Music", disk.Path("Music")).Id;
        int movies = AddCategory(db, "Movies", "Movie", disk.Path("Movies")).Id;
        var progress = new ListProgress();

        Scanner(db).ScanMediaCategories([music, movies], progress: progress);

        List<MediaScanProgress> reports = progress.Reports;
        // Reading, then saving, in order; the total is known once reading has finished.
        int firstSave = reports.FindIndex(r => r.Phase == MediaScanPhase.Saving);
        Assert.IsTrue(reports.Take(firstSave).All(r => r.Phase == MediaScanPhase.Reading && r.ItemsTotal == 0));
        Assert.AreEqual(6, reports.Max(r => r.FoldersRead), "Music, ABBA, Gold, Movies, Raw Blu-Rays and Alien, each counted once");
        Assert.IsTrue(reports.Skip(firstSave).All(r => r.ItemsTotal == 5), "1 artist, 1 album, 2 tracks, 1 movie");
        Assert.AreEqual(5, reports.Last(r => r.Phase == MediaScanPhase.Saving).ItemsDone);
        Assert.AreEqual(MediaScanPhase.Finished, reports.Last().Phase);
        CollectionAssert.IsSubsetOf(new[] { "Added artist ABBA", "Added album Gold", "Added track Waterloo", "Added movie Alien (1979)" },
            reports.Where(r => r.Change != null).Select(r => r.Change!.ToString()).ToList());
        CollectionAssert.AreEqual(new[] { "Movies", "Music" }, reports.Where(r => r.CategoryResult != null).Select(r => r.CategoryResult!.CategoryName).ToList());
        Assert.IsTrue(reports.Any(r => r.Message == "Scanned media category Movies: 1 added, 0 restored, 0 updated, 0 unchanged, 0 deleted"),
            string.Join("\n", reports.Select(r => r.Message)));
    }

    [TestMethod]
    public void TestDryRunSavesNothing()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        disk.File("Music", "ABBA", "Gold", "02 - Waterloo.flac");
        disk.File("Music", "ABBA", "Arrival", "01 - When I Kissed The Teacher.flac");
        int music = AddCategory(db, "Music", "Music", disk.Path("Music")).Id;
        Scanner(db).ScanAllMediaCategories();
        var tracks = new TrackService(db.ContextFactory);
        int waterloo = tracks.GetAllRecords("Waterloo").Single().Id;
        // On disk now: Waterloo has gone, Dancing Queen is an mp3, and there is a new artist.
        File.Delete(disk.Path("Music", "ABBA", "Gold", "02 - Waterloo.flac"));
        File.Move(disk.Path("Music", "ABBA", "Gold", "01 - Dancing Queen.flac"), disk.Path("Music", "ABBA", "Gold", "01 - Dancing Queen.mp3"));
        disk.File("Music", "Björk", "Homogenic", "05 - Jóga.mp3");
        var progress = new ListProgress();

        MediaScanResult result = Scanner(db).ScanMediaCategories([music], new MediaScanOptions { DryRun = true }, progress);

        Assert.IsTrue(result.IsDryRun);
        Assert.AreEqual((3, 0, 2, 1), (result.Added, result.Restored, result.Updated, result.Deleted), "Björk, Homogenic, Jóga; Gold's track count, Dancing Queen's file; Waterloo");
        CollectionAssert.IsSubsetOf(new[] { "Added artist Björk", "Added album Homogenic", "Added track Jóga", "Updated album Gold", "Updated track Dancing Queen", "Deleted track Waterloo" },
            progress.Reports.Where(r => r.Change != null).Select(r => r.Change!.ToString()).ToList());
        Assert.IsNotNull(tracks.GetRecordById(waterloo), "not deleted");
        Assert.AreEqual("01 - Dancing Queen.flac", tracks.GetAllRecords("Dancing Queen").Single().OriginalFilename, "not updated");
        Assert.IsEmpty(new ArtistService(db.ContextFactory).GetAllRecords("Björk"), "not added");
        Assert.AreEqual(6, Scanner(db).GetCategories().Single().LastScan!.Added, "the last scan is still the first one: a dry run isn't recorded");

        // A dry run also sees what would be restored.
        Scanner(db).ScanAllMediaCategories();
        disk.File("Music", "ABBA", "Gold", "02 - Waterloo.flac");
        MediaScanResult again = Scanner(db).ScanMediaCategories([music], new MediaScanOptions { DryRun = true });
        Assert.AreEqual((0, 1, 1, 0), (again.Added, again.Restored, again.Updated, again.Deleted), "Waterloo restored, Gold back to 2 tracks");
        Assert.IsNull(tracks.GetRecordById(waterloo), "still deleted");
    }

    [TestMethod]
    public void TestDeletionLimitHoldsBackLargeDeletions()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        foreach (string name in new[] { "Alien", "Heat", "Ronin", "Up" }) disk.File("Movies", "Raw Blu-Rays", name, name + ".mkv");
        int movies = AddCategory(db, "Movies", "Movie", disk.Path("Movies")).Id;
        Scanner(db).ScanAllMediaCategories();
        Directory.Delete(disk.Path("Movies", "Raw Blu-Rays", "Heat"), true);
        Directory.Delete(disk.Path("Movies", "Raw Blu-Rays", "Ronin"), true);
        disk.File("Movies", "Raw Blu-Rays", "Zulu", "Zulu.mkv");
        var logger = new CapturingLogger<MediaScanner>();

        // 2 of the 4 movies there before the scan would go (the new one doesn't count): 50%, over a 25% limit.
        MediaScanResult held = Scanner(db, logger).ScanMediaCategories([movies], new MediaScanOptions { MaxDeletePercent = 25 });

        MediaCategoryScanResult category = held.Categories.Single();
        Assert.AreEqual(MediaCategoryScanStatus.DeletionsHeld, category.Status);
        Assert.AreEqual((1, 0, 2), (held.Added, held.Deleted, category.DeletionsHeld), "Zulu is still added");
        Assert.AreEqual(1, held.CategoriesScanned);
        StringAssert.Contains(category.Message, "2 of 4 rows (50%)");
        Assert.IsNotNull(new MovieService(db.ContextFactory).GetRecordByName("Heat"));
        Assert.IsTrue(logger.Messages.Any(m => m.Contains("over the 25% limit")), string.Join("\n", logger.Messages));
        Assert.AreEqual(MediaCategoryScanStatus.DeletionsHeld, Scanner(db).GetCategories().Single().LastScan!.Status);

        // At the limit, they go.
        MediaScanResult deleted = Scanner(db).ScanMediaCategories([movies], new MediaScanOptions { MaxDeletePercent = 40 });
        Assert.AreEqual(MediaCategoryScanStatus.Scanned, deleted.Categories.Single().Status, "2 of 5 rows = 40%");
        Assert.AreEqual(2, deleted.Deleted);
        Assert.IsNull(new MovieService(db.ContextFactory).GetRecordByName("Heat"));
    }

    [TestMethod]
    public void TestClearDataNeedsEveryActiveCategory()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        Directory.CreateDirectory(disk.Path("Movies"));
        int music = AddCategory(db, "Music", "Music", disk.Path("Music")).Id;
        AddCategory(db, "Movies", "Movie", disk.Path("Movies"));
        AddCategory(db, "Books", "Audiobooks", disk.Path("Books"));
        Scanner(db).ScanAllMediaCategories();

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => Scanner(db).ScanMediaCategories([music], new MediaScanOptions { ClearData = true }));

        StringAssert.Contains(error.Message, "not selected: Movies", "Books can't be scanned, so it isn't needed");
        Assert.HasCount(1, new TrackService(db.ContextFactory).GetAllRecords(), "nothing was cleared");
        // A dry run ignores ClearData.
        Assert.AreEqual(0, Scanner(db).ScanMediaCategories([music], new MediaScanOptions { ClearData = true, DryRun = true }).Added);
    }

    [TestMethod]
    public void TestGetCategoriesWithTheirLastScan()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        int movies = AddCategory(db, "Movies", "Movie", disk.Path("Movies")).Id;
        int books = AddCategory(db, "Books", "Audiobooks", disk.Path("Books")).Id;
        int offline = AddCategory(db, "Offline", "TV", disk.Path("Not There")).Id;

        Scanner(db).ScanAllMediaCategories();
        List<MediaScanCategory> categories = Scanner(db).GetCategories();

        MediaScanCategory movieCategory = categories.Single(c => c.Id == movies);
        Assert.IsTrue(movieCategory.CanScan);
        Assert.AreEqual(MediaKind.Movie, movieCategory.Kind);
        Assert.AreEqual((MediaCategoryScanStatus.Scanned, 1), (movieCategory.LastScan!.Status, movieCategory.LastScan.Added));
        Assert.AreEqual(DateTime.Today, movieCategory.LastScan.FinishedOn.Date);
        MediaScanCategory bookCategory = categories.Single(c => c.Id == books);
        Assert.IsFalse(bookCategory.CanScan);
        StringAssert.Contains(bookCategory.CannotScanReason, "isn't Music, Movie or TV");
        MediaScanCategory offlineCategory = categories.Single(c => c.Id == offline);
        Assert.IsTrue(offlineCategory.CanScan, "whether the folder is there is found out when it is scanned");
        Assert.AreEqual(MediaCategoryScanStatus.Skipped, offlineCategory.LastScan!.Status);
        StringAssert.Contains(offlineCategory.LastScan.Message, "can't be reached");
    }

    [TestMethod]
    public void TestOnlyOneScanAtATime()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File("Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        int movies = AddCategory(db, "Movies", "Movie", disk.Path("Movies")).Id;
        MediaScanner scanner = Scanner(db);
        Exception? second = null;
        var progress = new ListProgress(_ =>
        {
            if (second != null || !scanner.IsScanning) return;
            second = Assert.ThrowsExactly<InvalidOperationException>(() => scanner.ScanMediaCategories([movies]));
        });

        scanner.ScanMediaCategories([movies], progress: progress);

        StringAssert.Contains(second!.Message, "already running");
        Assert.IsFalse(scanner.IsScanning);
        Assert.AreEqual(0, scanner.ScanMediaCategories([movies]).Added, "and it can run again afterwards");
    }

    [TestMethod]
    public void TestScanSavesFileSizesAndDates()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempMedia();
        disk.File(300, "Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        disk.File(5000, "Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        disk.File(70, "Movies", "Raw Blu-Rays", "Alien (1979)", "Sample.mkv");
        disk.File(1200, "TV", "Firefly (2002)", "Season 01", "Firefly (2002) - s01e01 - Serenity.mkv");
        var modified = new DateTime(2024, 5, 6, 7, 8, 9);
        System.IO.File.SetLastWriteTime(disk.Path("Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv"), modified);
        AddCategory(db, "Music", "Music", disk.Path("Music"));
        AddCategory(db, "Movies", "Movie", disk.Path("Movies"));
        AddCategory(db, "TV", "TV", disk.Path("TV"));

        Scanner(db).ScanAllMediaCategories();

        Movie alien = new MovieService(db.ContextFactory).GetRecordByName("Alien (1979)")!;
        Assert.AreEqual(5000L, alien.FileSize, "the largest video file's size");
        Assert.AreEqual(modified, alien.FileDate);
        Assert.AreEqual(300L, new TrackService(db.ContextFactory).GetAllRecords("Dancing Queen").Single().FileSize);
        Assert.AreEqual(1200L, new TVShowEpisodeService(db.ContextFactory).GetAllRecords("Serenity").Single().FileSize);

        // A file that changes (e.g. re-encoded) is updated by the next scan; nothing else is.
        disk.File(2000, "Movies", "Raw Blu-Rays", "Alien (1979)", "Alien (1979).mkv");
        MediaScanResult rescan = Scanner(db).ScanAllMediaCategories();

        Assert.AreEqual(1, rescan.Updated);
        Assert.AreEqual(2000L, new MovieService(db.ContextFactory).GetRecordByName("Alien (1979)")!.FileSize);
    }

    [TestMethod]
    public void TestUpgradeScriptAddsTheFileColumns()
    {
        using var db = new SqlServerTestDatabase();
        const string columns = "SELECT COUNT(*) FROM sys.columns WHERE name IN ('FileSize', 'FileDate') AND object_id IN "
            + "(OBJECT_ID('dbo.Tracks'), OBJECT_ID('dbo.Movies'), OBJECT_ID('dbo.TVShowEpisodes'))";
        db.Execute("INSERT INTO Movies (Name) VALUES ('Alien (1979)')");
        db.Execute("""
            ALTER TABLE Tracks DROP COLUMN FileSize, FileDate;
            ALTER TABLE Movies DROP COLUMN FileSize, FileDate;
            ALTER TABLE TVShowEpisodes DROP COLUMN FileSize, FileDate;
            """);

        List<string> firstRun = db.ExecuteScript(Path.Combine("Schema", "Upgrades", "003-File-size-and-date.sql"));
        List<string> secondRun = db.ExecuteScript(Path.Combine("Schema", "Upgrades", "003-File-size-and-date.sql"));

        Assert.IsEmpty(firstRun, string.Join("\n", firstRun));
        Assert.AreEqual(6, db.Scalar<int>(columns));
        Assert.AreEqual(1, db.Scalar<int>("SELECT COUNT(*) FROM Movies WHERE FileSize IS NULL"), "existing rows are kept");
        Assert.HasCount(1, secondRun);
        StringAssert.Contains(secondRun[0], "already been applied");
    }

    // Helpers.
    // ==================================================================================================

    private static MediaScanner Scanner(SqlServerTestDatabase db, ILogger<MediaScanner>? logger = null) =>
        new(new MediaCategoryService(db.ContextFactory), new ArtistService(db.ContextFactory), new AlbumService(db.ContextFactory),
            new AlbumTypeService(db.ContextFactory), new TrackService(db.ContextFactory), new MovieService(db.ContextFactory),
            new TVShowService(db.ContextFactory), new TVShowEpisodeService(db.ContextFactory), new MediaCacheService(db.ContextFactory), new SettingService(db.ContextFactory), logger);

    private static MediaCategory AddCategory(SqlServerTestDatabase db, string name, string mediaClass, string rootFolder, int albumTypeId = 5) =>
        new MediaCategoryService(db.ContextFactory).AddRecord(new MediaCategory { Name = name, MediaClass = mediaClass, RootFolder = rootFolder, AlbumTypeId = albumTypeId });

    /// <summary>A folder tree of media files in a temp folder, deleted on dispose.</summary>
    private sealed class TempMedia : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pda_scan_test_" + Guid.NewGuid().ToString("N"));

        public string Path(params string[] parts) => System.IO.Path.Combine([_root, .. parts]);

        /// <summary>Creates a one-byte file (and its folders) at the path made of <paramref name="parts"/>.</summary>
        public void File(params string[] parts) => Create(parts, 1);

        /// <summary>Creates a file of <paramref name="size"/> bytes (and its folders) at the path made of <paramref name="parts"/>.</summary>
        public void File(int size, params string[] parts) => Create(parts, size);

        private void Create(string[] parts, int size)
        {
            string path = Path(parts);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, new byte[size]);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    /// <summary>Keeps every progress report, in order, as it is made (Progress&lt;T&gt; would post them to the thread pool).</summary>
    private sealed class ListProgress(Action<MediaScanProgress>? onReport = null) : IProgress<MediaScanProgress>
    {
        public List<MediaScanProgress> Reports { get; } = [];

        public void Report(MediaScanProgress value)
        {
            Reports.Add(value);
            onReport?.Invoke(value);
        }
    }

    /// <summary>An ILogger that keeps the formatted messages, for checking what a class logged.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
