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

    // Helpers.
    // ==================================================================================================

    private static MediaScanner Scanner(SqlServerTestDatabase db, ILogger<MediaScanner>? logger = null) =>
        new(new MediaCategoryService(db.ContextFactory), new ArtistService(db.ContextFactory), new AlbumService(db.ContextFactory),
            new AlbumTypeService(db.ContextFactory), new TrackService(db.ContextFactory), new MovieService(db.ContextFactory),
            new TVShowService(db.ContextFactory), new TVShowEpisodeService(db.ContextFactory), new MediaCacheService(db.ContextFactory), logger);

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
