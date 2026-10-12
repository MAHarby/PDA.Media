using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;

namespace PDA.Media.Tests;

/// <summary>
/// Clearing the media cache (dbo.ClearMediaCache through MediaCacheService), and its upgrade script.
/// Against a real SQL Server database (see SqlServerTestDatabase; Inconclusive without one).
/// </summary>
[TestClass]
public sealed class MediaCacheServiceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestClearMediaCache(bool async)
    {
        using var db = new SqlServerTestDatabase();
        Seed(db, out int lastAlbumId);
        var service = new MediaCacheService(db.ContextFactory);

        MediaCacheCleared cleared = async ? await service.ClearMediaCacheAsync() : service.ClearMediaCache();

        Assert.AreEqual(2, cleared.Artists, "every artist except Artist 0");
        Assert.AreEqual(2, cleared.Albums, "soft-deleted albums too");
        Assert.AreEqual(3, cleared.Tracks);
        Assert.AreEqual(1, cleared.Movies);
        Assert.AreEqual(1, cleared.TVShows);
        Assert.AreEqual(2, cleared.TVShowEpisodes);
        Assert.AreEqual(11, cleared.Total);

        // The cache tables are empty apart from Artist 0.
        foreach (string table in new[] { "Albums", "Tracks", "Movies", "TVShows", "TVShowEpisodes" })
            Assert.AreEqual(0, db.Scalar<int>($"SELECT COUNT(*) FROM {table}"), table);
        Assert.AreEqual("Unknown Artist", db.Scalar<string>("SELECT Name FROM Artists WHERE Id = 0"));
        Assert.AreEqual(1, db.Scalar<int>("SELECT COUNT(*) FROM Artists"));

        // Lookup and configuration tables are kept.
        Assert.AreEqual(2, db.Scalar<int>("SELECT COUNT(*) FROM MovieTypes"));
        Assert.AreEqual(1, db.Scalar<int>("SELECT COUNT(*) FROM MediaCategories"));
        Assert.AreEqual(@"\\nas", db.Scalar<string>("SELECT Value FROM Settings WHERE [Key] = 'SourceFolder'"));

        // Ids carry on rather than restarting.
        Album next = new AlbumService(db.ContextFactory).AddRecord(new Album { Name = "Voyage" });
        Assert.IsGreaterThan(lastAlbumId, next.Id);
    }

    [TestMethod]
    public void TestClearingAnEmptyCache()
    {
        using var db = new SqlServerTestDatabase();
        var service = new MediaCacheService(db.ContextFactory);

        MediaCacheCleared cleared = service.ClearMediaCache();

        Assert.AreEqual(0, cleared.Total);
        Assert.AreEqual(1, db.Scalar<int>("SELECT COUNT(*) FROM Artists WHERE Id = 0"));
    }

    [TestMethod]
    public void TestUpgradeScriptCreatesTheSameProcedure()
    {
        using var db = new SqlServerTestDatabase();
        const string definition = "SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.ClearMediaCache'))";
        string fromSchema = db.Scalar<string>(definition);
        db.Execute("DROP PROCEDURE dbo.ClearMediaCache");

        List<string> firstRun = db.ExecuteScript(Path.Combine("Schema", "Upgrades", "002-ClearMediaCache.sql"));
        string fromUpgrade = db.Scalar<string>(definition);
        List<string> secondRun = db.ExecuteScript(Path.Combine("Schema", "Upgrades", "002-ClearMediaCache.sql"));

        Assert.IsEmpty(firstRun, string.Join("\n", firstRun));
        Assert.AreEqual(fromSchema, fromUpgrade, "the upgrade and Media.Master.sql must create the same procedure");
        // A second run stops at its check: one error, and the CREATE PROCEDURE never runs.
        Assert.HasCount(1, secondRun);
        StringAssert.Contains(secondRun[0], "already been applied");
        Assert.AreEqual(fromSchema, db.Scalar<string>(definition));
    }

    /// <summary>Two artists (plus Artist 0), two albums (one soft-deleted), three tracks, a movie, a show with two episodes, and lookup data.</summary>
    private static void Seed(SqlServerTestDatabase db, out int lastAlbumId)
    {
        var artists = new ArtistService(db.ContextFactory);
        var albums = new AlbumService(db.ContextFactory);
        var tracks = new TrackService(db.ContextFactory);
        Artist abba = artists.AddRecord(new Artist { Name = "ABBA" });
        artists.AddRecord(new Artist { Name = "Björk" });
        Album gold = albums.AddRecord(new Album { ArtistId = abba.Id, Name = "Gold" });
        Album arrival = albums.AddRecord(new Album { ArtistId = abba.Id, Name = "Arrival" });
        for (int i = 1; i <= 3; i++) tracks.AddRecord(new Track { AlbumId = gold.Id, ArtistId = abba.Id, TrackNo = i, Name = $"Track {i}" });
        albums.DeleteRecord(arrival.Id);

        MovieType feature = new MovieTypeService(db.ContextFactory).AddRecord(new MovieType { Name = "Feature" });
        new MovieService(db.ContextFactory).AddRecord(new Movie { Name = "Alien (1979)", MovieTypeId = feature.Id });
        TVShow office = new TVShowService(db.ContextFactory).AddRecord(new TVShow { Name = "The Office (US)", ReleaseYear = 2005 });
        var episodes = new TVShowEpisodeService(db.ContextFactory);
        episodes.AddRecord(new TVShowEpisode { TVShowId = office.Id, SeasonNo = 1, EpisodeNo = 1, Name = "Pilot" });
        episodes.AddRecord(new TVShowEpisode { TVShowId = office.Id, SeasonNo = 1, EpisodeNo = 2, Name = "Diversity Day" });

        new MediaCategoryService(db.ContextFactory).AddRecord(new MediaCategory { Name = "Movies", MediaClass = "Movie" });
        new SettingService(db.ContextFactory).SetValue("SourceFolder", @"\\nas");
        lastAlbumId = arrival.Id;
    }
}
