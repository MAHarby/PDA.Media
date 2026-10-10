using Microsoft.Extensions.DependencyInjection;
using PDA.Media.Data;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;

namespace PDA.Media.Tests;

/// <summary>
/// The data services other than AlbumService (which AlbumServiceTests covers, including the shared DataService
/// behaviour in depth). Against a real SQL Server database (see SqlServerTestDatabase; Inconclusive without one).
/// Tests with an "async" DataRow run once through the synchronous methods and once through the async ones.
/// </summary>
[TestClass]
public sealed class DataServiceTests
{
    // Shared behaviour on other kinds of entity.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestDeletingALookupRowMovesItsRowsToTheDefault(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var types = new MovieTypeService(db.ContextFactory);
        var movies = new MovieService(db.ContextFactory);
        MovieType documentary = types.AddRecord(new MovieType { Name = "Documentary" });
        Movie movie = movies.AddRecord(new Movie { Name = "Free Solo (2018)", MovieTypeId = documentary.Id });

        bool deleted = async ? await types.DeleteRecordAsync(documentary.Id) : types.DeleteRecord(documentary.Id);

        // Lookup rows are removed, not soft-deleted; the database moves the movie to type 0 (ON DELETE SET DEFAULT).
        Assert.IsTrue(deleted);
        Assert.AreEqual(0, db.Scalar<int>($"SELECT COUNT(*) FROM MovieTypes WHERE Id = {documentary.Id}"));
        Assert.AreEqual(0, movies.GetRecordById(movie.Id)!.MovieTypeId);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestDefaultRowsCantBeDeleted(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var deletes = new (string Name, Func<Task<bool>> Async, Func<bool> Sync)[]
        {
            ("Artist 0", () => new ArtistService(db.ContextFactory).DeleteRecordAsync(0), () => new ArtistService(db.ContextFactory).DeleteRecord(0)),
            ("AlbumType 1", () => new AlbumTypeService(db.ContextFactory).DeleteRecordAsync(1), () => new AlbumTypeService(db.ContextFactory).DeleteRecord(1)),
            ("MovieType 0", () => new MovieTypeService(db.ContextFactory).DeleteRecordAsync(0), () => new MovieTypeService(db.ContextFactory).DeleteRecord(0)),
            ("TVShowType 0", () => new TVShowTypeService(db.ContextFactory).DeleteRecordAsync(0), () => new TVShowTypeService(db.ContextFactory).DeleteRecord(0)),
        };

        foreach (var (name, deleteAsync, delete) in deletes)
        {
            if (async) await Assert.ThrowsExactlyAsync<InvalidOperationException>(deleteAsync, name);
            else Assert.ThrowsExactly<InvalidOperationException>(() => delete(), name);
        }

        Assert.AreEqual(1, db.Scalar<int>("SELECT COUNT(*) FROM Artists WHERE Id = 0 AND IsDeleted = 0"));
        Assert.AreEqual(1, db.Scalar<int>("SELECT COUNT(*) FROM AlbumTypes WHERE Id = 1"));
        Assert.AreEqual(1, db.Scalar<int>("SELECT COUNT(*) FROM MovieTypes WHERE Id = 0"));
        Assert.AreEqual(1, db.Scalar<int>("SELECT COUNT(*) FROM TVShowTypes WHERE Id = 0"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestUpdateRecordWithoutAuditFields(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new TVShowTypeService(db.ContextFactory);
        TVShowType added = service.AddRecord(new TVShowType { Name = "Series" });

        var changes = new TVShowType { Name = "Mini-series", Description = "One season" };
        TVShowType? updated = async ? await service.UpdateRecordAsync(added.Id, changes) : service.UpdateRecord(added.Id, changes);

        Assert.AreEqual(added.Id, updated!.Id);
        TVShowType saved = service.GetRecordById(added.Id)!;
        Assert.AreEqual("Mini-series", saved.Name);
        Assert.AreEqual("One season", saved.Description);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestSearchOtherEntities(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new MovieService(db.ContextFactory);
        foreach (string name in new[] { "Alien (1979)", "Aliens (1986)", "千と千尋の神隠し (2001)", "Blade Runner (1982)" })
            service.AddRecord(new Movie { Name = name });

        List<Movie> aliens = async ? await service.GetAllRecordsAsync("Alien*") : service.GetAllRecords("Alien*");
        List<Movie> unicode = async ? await service.GetAllRecordsAsync("千と千尋") : service.GetAllRecords("千と千尋");

        CollectionAssert.AreEqual(new[] { "Alien (1979)", "Aliens (1986)" }, aliens.Select(m => m.Name).ToArray());
        Assert.AreEqual("千と千尋の神隠し (2001)", unicode.Single().Name);
    }

    // Artists.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestArtists(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new ArtistService(db.ContextFactory);
        Artist abba = service.AddRecord(new Artist { Name = "ABBA" });

        Artist again = async ? await service.AddRecordAsync(new Artist { Name = "Abba" }) : service.AddRecord(new Artist { Name = "Abba" });
        Artist? byName = async ? await service.GetRecordByNameAsync("abba") : service.GetRecordByName("abba");

        Assert.AreEqual(abba.Id, again.Id, "same name (ignoring case) is the same artist");
        Assert.AreEqual(abba.Id, byName!.Id);
        CollectionAssert.AreEqual(new[] { "ABBA", "Unknown Artist" }, service.GetAllRecords().Select(a => a.Name).ToArray());
    }

    // Tracks.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestTracks(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var artists = new ArtistService(db.ContextFactory);
        var albums = new AlbumService(db.ContextFactory);
        var service = new TrackService(db.ContextFactory);
        Artist abba = artists.AddRecord(new Artist { Name = "ABBA" });
        Album gold = albums.AddRecord(new Album { ArtistId = abba.Id, Name = "Gold" });
        Album arrival = albums.AddRecord(new Album { ArtistId = abba.Id, Name = "Arrival" });
        service.AddRecord(new Track { AlbumId = gold.Id, ArtistId = abba.Id, TrackNo = 2, Name = "Knowing Me, Knowing You" });
        service.AddRecord(new Track { AlbumId = gold.Id, ArtistId = abba.Id, TrackNo = 1, Name = "Dancing Queen" });
        service.AddRecord(new Track { AlbumId = arrival.Id, ArtistId = abba.Id, TrackNo = 1, Name = "When I Kissed the Teacher" });

        // Same album, number and name: the existing track. Same name with another number: a new track (e.g. a reprise).
        var duplicate = new Track { AlbumId = gold.Id, ArtistId = abba.Id, TrackNo = 1, Name = "Dancing Queen" };
        var reprise = new Track { AlbumId = gold.Id, ArtistId = abba.Id, TrackNo = 3, Name = "Dancing Queen" };
        Track first = async ? await service.AddRecordAsync(duplicate) : service.AddRecord(duplicate);
        Track third = async ? await service.AddRecordAsync(reprise) : service.AddRecord(reprise);

        List<Track> onGold = async ? await service.GetAllRecordsByAlbumIdAsync(gold.Id) : service.GetAllRecordsByAlbumId(gold.Id);
        List<Track> byAbba = async ? await service.GetAllRecordsByArtistIdAsync(abba.Id) : service.GetAllRecordsByArtistId(abba.Id);

        Assert.AreNotEqual(first.Id, third.Id);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, onGold.Select(t => t.TrackNo).ToArray(), "in track order");
        Assert.HasCount(4, byAbba);
    }

    // Movies.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestMovies(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var types = new MovieTypeService(db.ContextFactory);
        var service = new MovieService(db.ContextFactory);
        MovieType feature = types.AddRecord(new MovieType { Name = "Feature" });
        Movie alien = service.AddRecord(new Movie { Name = "Alien (1979)", MovieTypeId = feature.Id });
        service.AddRecord(new Movie { Name = "Unsorted (2020)" });

        Movie again = async ? await service.AddRecordAsync(new Movie { Name = "Alien (1979)" }) : service.AddRecord(new Movie { Name = "Alien (1979)" });
        Movie? byName = async ? await service.GetRecordByNameAsync("Alien (1979)") : service.GetRecordByName("Alien (1979)");
        List<Movie> features = async ? await service.GetAllRecordsByMovieTypeIdAsync(feature.Id) : service.GetAllRecordsByMovieTypeId(feature.Id);

        Assert.AreEqual(alien.Id, again.Id);
        Assert.AreEqual(alien.Id, byName!.Id);
        Assert.AreEqual(alien.Id, features.Single().Id);
        Assert.AreEqual("Feature", (async ? await types.GetRecordByNameAsync("feature") : types.GetRecordByName("feature"))!.Name);
    }

    // TV shows and episodes.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestTVShows(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var types = new TVShowTypeService(db.ContextFactory);
        var service = new TVShowService(db.ContextFactory);
        TVShowType series = types.AddRecord(new TVShowType { Name = "Series" });
        TVShow classic = service.AddRecord(new TVShow { Name = "Doctor Who", ReleaseYear = 1963, TVShowTypeId = series.Id });

        // Same name, another year: a different show. Same name and year: the existing show.
        var revival = new TVShow { Name = "Doctor Who", ReleaseYear = 2005, TVShowTypeId = series.Id };
        TVShow added = async ? await service.AddRecordAsync(revival) : service.AddRecord(revival);
        TVShow again = async ? await service.AddRecordAsync(new TVShow { Name = "Doctor Who", ReleaseYear = 1963 }) : service.AddRecord(new TVShow { Name = "Doctor Who", ReleaseYear = 1963 });
        TVShow? byName = async ? await service.GetRecordByNameAsync("Doctor Who", 2005) : service.GetRecordByName("Doctor Who", 2005);
        List<TVShow> ofType = async ? await service.GetAllRecordsByTVShowTypeIdAsync(series.Id) : service.GetAllRecordsByTVShowTypeId(series.Id);

        Assert.AreNotEqual(classic.Id, added.Id);
        Assert.AreEqual(classic.Id, again.Id);
        Assert.AreEqual(added.Id, byName!.Id);
        Assert.HasCount(2, ofType);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestTVShowEpisodes(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var shows = new TVShowService(db.ContextFactory);
        var service = new TVShowEpisodeService(db.ContextFactory);
        TVShow office = shows.AddRecord(new TVShow { Name = "The Office (US)", ReleaseYear = 2005 });
        foreach (var (season, episode, name) in new[] { (2, 1, "The Dundies"), (1, 2, "Diversity Day"), (1, 1, "Pilot"), (0, 1, "Christmas Special") })
            service.AddRecord(new TVShowEpisode { TVShowId = office.Id, SeasonNo = season, EpisodeNo = episode, Name = name });

        // Same show, season and episode: the existing episode, whatever its name.
        var renamed = new TVShowEpisode { TVShowId = office.Id, SeasonNo = 1, EpisodeNo = 1, Name = "Pilot (Extended)" };
        TVShowEpisode pilot = async ? await service.AddRecordAsync(renamed) : service.AddRecord(renamed);
        // Episodes whose number couldn't be read (0) are told apart by name.
        var unknownA = new TVShowEpisode { TVShowId = office.Id, SeasonNo = 3, Name = "Unknown A" };
        var unknownB = new TVShowEpisode { TVShowId = office.Id, SeasonNo = 3, Name = "Unknown B" };
        TVShowEpisode a = async ? await service.AddRecordAsync(unknownA) : service.AddRecord(unknownA);
        TVShowEpisode b = async ? await service.AddRecordAsync(unknownB) : service.AddRecord(unknownB);
        TVShowEpisode aAgain = async ? await service.AddRecordAsync(new TVShowEpisode { TVShowId = office.Id, SeasonNo = 3, Name = "Unknown A" })
            : service.AddRecord(new TVShowEpisode { TVShowId = office.Id, SeasonNo = 3, Name = "Unknown A" });

        List<TVShowEpisode> all = async ? await service.GetAllRecordsByTVShowIdAsync(office.Id) : service.GetAllRecordsByTVShowId(office.Id);
        List<TVShowEpisode> season1 = async ? await service.GetAllRecordsBySeasonAsync(office.Id, 1) : service.GetAllRecordsBySeason(office.Id, 1);
        TVShowEpisode? s2e1 = async ? await service.GetRecordByEpisodeAsync(office.Id, 2, 1) : service.GetRecordByEpisode(office.Id, 2, 1);

        Assert.AreEqual("Pilot", pilot.Name);
        Assert.AreNotEqual(a.Id, b.Id);
        Assert.AreEqual(a.Id, aAgain.Id);
        CollectionAssert.AreEqual(new[] { "Christmas Special", "Pilot", "Diversity Day", "The Dundies", "Unknown A", "Unknown B" },
            all.Select(e => e.Name).ToArray(), "in season and episode order");
        CollectionAssert.AreEqual(new[] { "Pilot", "Diversity Day" }, season1.Select(e => e.Name).ToArray());
        Assert.AreEqual("The Dundies", s2e1!.Name);
    }

    // Media categories.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestMediaCategories(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new MediaCategoryService(db.ContextFactory);
        MediaCategory movies = service.AddRecord(new MediaCategory { Name = "Movies", MediaClass = "Movie", RootFolder = @"\\nas\Movies" });
        service.AddRecord(new MediaCategory { Name = "Old Movies", MediaClass = "Movie", IsActive = false });
        MediaCategory music = service.AddRecord(new MediaCategory { Name = "Music", MediaClass = "Music" });

        List<MediaCategory> active = async ? await service.GetActiveRecordsAsync() : service.GetActiveRecords();
        List<MediaCategory> ofClass = async ? await service.GetAllRecordsByMediaClassAsync("Movie") : service.GetAllRecordsByMediaClass("Movie");
        bool deleted = async ? await service.DeleteRecordAsync(music.Id) : service.DeleteRecord(music.Id);

        CollectionAssert.AreEqual(new[] { "Movies", "Music" }, active.Select(c => c.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Movies", "Old Movies" }, ofClass.Select(c => c.Name).ToArray());
        Assert.AreEqual(@"\\nas\Movies", service.GetRecordByName("movies")!.RootFolder);
        // Categories are soft-deleted: hidden, but the row stays.
        Assert.IsTrue(deleted);
        Assert.IsNull(service.GetRecordById(music.Id));
        Assert.IsTrue(db.Scalar<bool>($"SELECT IsDeleted FROM MediaCategories WHERE Id = {music.Id}"));
        Assert.AreEqual(movies.Id, service.GetAllRecords().First().Id);
    }

    // Settings.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestSettings(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new SettingService(db.ContextFactory);

        if (async)
        {
            await service.SetValueAsync("LastScan", "2026-10-01");
            await service.SetValueAsync("lastscan", "2026-10-10");   // keys ignore case: this updates the same setting
            await service.SetValueAsync("SourceFolder", @"\\nas\Media\Мумий Тролль");
        }
        else
        {
            service.SetValue("LastScan", "2026-10-01");
            service.SetValue("lastscan", "2026-10-10");
            service.SetValue("SourceFolder", @"\\nas\Media\Мумий Тролль");
        }

        string? lastScan = async ? await service.GetValueAsync("LASTSCAN") : service.GetValue("LASTSCAN");
        string? missing = async ? await service.GetValueAsync("Nope") : service.GetValue("Nope");
        Dictionary<string, string> all = async ? await service.GetAllValuesAsync() : service.GetAllValues();
        bool deleted = async ? await service.DeleteValueAsync("LastScan") : service.DeleteValue("LastScan");
        bool deletedAgain = async ? await service.DeleteValueAsync("LastScan") : service.DeleteValue("LastScan");

        Assert.AreEqual("2026-10-10", lastScan);
        Assert.IsNull(missing);
        Assert.HasCount(2, all);
        Assert.AreEqual(@"\\nas\Media\Мумий Тролль", all["sourcefolder"]);
        Assert.IsTrue(deleted);
        Assert.IsFalse(deletedAgain);
        Assert.IsNull(service.GetValue("LastScan"));
    }

    // Registration.
    // ==================================================================================================

    [TestMethod]
    public void TestAddMediaDataRegistersEveryService()
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddMediaData(DataConnection.BuildConnectionString("TestServer", "TestDb")).BuildServiceProvider();

        foreach (var type in new[]
        {
            typeof(AlbumService), typeof(AlbumTypeService), typeof(ArtistService), typeof(MediaCategoryService),
            typeof(MovieService), typeof(MovieTypeService), typeof(SettingService), typeof(TrackService),
            typeof(TVShowService), typeof(TVShowEpisodeService), typeof(TVShowTypeService), typeof(DatabaseStatusService),
        })
        {
            Assert.IsNotNull(provider.GetService(type), type.Name);
        }
    }
}
