using System.IO;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;

namespace PDA.Media.Tests;

/// <summary>MediaSearchService against a real SQL Server database (see SqlServerTestDatabase; Inconclusive without one).</summary>
[TestClass]
public sealed class MediaSearchServiceTests
{
    private static readonly string Music = Path.Combine("nas", "Music");
    private static readonly string Live = Path.Combine("nas", "Live");
    private static readonly string Movies = Path.Combine("nas", "Movies");
    private static readonly string TV = Path.Combine("nas", "TV");

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestSearchFindsNameDescriptionAndNotesAtEveryLevel(bool async)
    {
        using var db = new SqlServerTestDatabase();
        Library lib = Library.Add(db);
        var service = new MediaSearchService(db.ContextFactory);

        MediaSearchResult byTrack = await Search(service, async, new MediaSearchCriteria { Text = "bohemian", MusicRoots = [Music, Live] });
        MediaSearchResult byNotes = await Search(service, async, new MediaSearchCriteria { Text = "*vinyl*", MusicRoots = [Music], MovieRoots = [Movies], TVRoots = [TV] });
        MediaSearchResult byEpisode = await Search(service, async, new MediaSearchCriteria { Text = "Serenity", TVRoots = [TV] });

        // A track match brings its artist (top level) and album (to open).
        Assert.AreEqual("Queen", byTrack.Artists.Single().Name);
        Assert.AreEqual(lib.Opera.Id, byTrack.PathAlbums.Single().Id);
        Assert.AreEqual("Bohemian Rhapsody", byTrack.MatchedTracks.Single().Name);
        Assert.IsEmpty(byTrack.MatchedArtistIds, "the artist itself didn't match");
        // Notes and descriptions match too, in each kind of media.
        CollectionAssert.AreEquivalent(new[] { lib.Opera.Id }, byNotes.MatchedAlbumIds.ToArray());
        Assert.AreEqual("Heat (1995)", byNotes.Movies.Single().Name, "its description");
        Assert.IsEmpty(byNotes.Shows);
        // An episode match brings its show.
        Assert.AreEqual("Firefly", byEpisode.Shows.Single().Name);
        Assert.AreEqual("Serenity", byEpisode.MatchedEpisodes.Single().Name);
        Assert.IsFalse(byTrack.IsBrowse || byTrack.IsTruncated);
    }

    [TestMethod]
    public void TestCategoriesAreChosenByRootFolder()
    {
        using var db = new SqlServerTestDatabase();
        Library lib = Library.Add(db);
        var service = new MediaSearchService(db.ContextFactory);

        // Queen's own folder is in Music, but it has an album in Live too: it is found in either category, with that category's albums.
        MediaSearchResult live = service.Search(new MediaSearchCriteria { Text = "Queen", MusicRoots = [Live] });
        Assert.AreEqual(lib.Queen.Id, live.Artists.Single().Id);
        CollectionAssert.AreEqual(new[] { "Live Killers" }, service.GetArtistAlbums(lib.Queen.Id, [Live]).Select(a => a.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "A Night at the Opera", "Live Killers" }, service.GetArtistAlbums(lib.Queen.Id, [Music, Live]).Select(a => a.Name).ToArray());

        // A root only covers its own folder: "nas/Music" doesn't include "nas/Music2".
        MediaSearchResult music = service.Search(new MediaSearchCriteria { MusicRoots = [Music] });
        CollectionAssert.AreEqual(new[] { "ABBA", "Queen" }, music.Artists.Select(a => a.Name).ToArray());
        Assert.IsTrue(music.IsBrowse, "no text: everything under the roots");
        Assert.IsEmpty(service.Search(new MediaSearchCriteria { Text = "Queen" }).Artists, "no roots, nothing searched");
    }

    [TestMethod]
    public void TestFavouritesAndDeletedRows()
    {
        using var db = new SqlServerTestDatabase();
        Library lib = Library.Add(db);
        var service = new MediaSearchService(db.ContextFactory);
        new MovieService(db.ContextFactory).DeleteRecord(lib.Alien.Id);
        new TrackService(db.ContextFactory).DeleteRecord(lib.Waterloo.Id);

        MediaSearchResult favourites = service.Search(new MediaSearchCriteria { FavouritesOnly = true, MusicRoots = [Music], MovieRoots = [Movies], TVRoots = [TV] });
        MediaSearchResult live = service.Search(new MediaSearchCriteria { MovieRoots = [Movies] });
        MediaSearchResult withDeleted = service.Search(new MediaSearchCriteria { MovieRoots = [Movies], IncludeDeleted = true });

        Assert.AreEqual("Firefly", favourites.Shows.Single().Name);
        Assert.IsEmpty(favourites.Artists);
        Assert.AreEqual("Heat (1995)", live.Movies.Single().Name);
        CollectionAssert.AreEqual(new[] { "Alien", "Heat (1995)" }, withDeleted.Movies.Select(m => m.Name).ToArray());
        Assert.HasCount(1, service.GetAlbumTracks(lib.Gold.Id));
        Assert.HasCount(2, service.GetAlbumTracks(lib.Gold.Id, includeDeleted: true));
    }

    [TestMethod]
    public void TestChildrenAreInOrder()
    {
        using var db = new SqlServerTestDatabase();
        Library lib = Library.Add(db);
        var service = new MediaSearchService(db.ContextFactory);

        CollectionAssert.AreEqual(new[] { "Dancing Queen", "Waterloo" }, service.GetAlbumTracks(lib.Gold.Id).Select(t => t.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "1x1 Serenity", "1x2 The Train Job", "2x1 Pilot" },
            service.GetShowEpisodes(lib.Firefly.Id).Select(e => $"{e.SeasonNo}x{e.EpisodeNo} {e.Name}").ToArray());
    }

    [TestMethod]
    public void TestTypesNarrowEachKindOfMedia()
    {
        using var db = new SqlServerTestDatabase();
        Library lib = Library.Add(db);
        var f = db.ContextFactory;
        int live = new AlbumTypeService(f).AddRecord(new AlbumType { Name = "Live" }).Id;
        int compilation = new AlbumTypeService(f).AddRecord(new AlbumType { Name = "Compilation" }).Id;
        var albums = new AlbumService(f);
        Album killers = albums.GetAllRecords("Live Killers").Single();
        (killers.AlbumTypeId, lib.Gold.AlbumTypeId) = (live, compilation);
        albums.UpdateRecord(killers.Id, killers);
        albums.UpdateRecord(lib.Gold.Id, lib.Gold);
        int concert = new MovieTypeService(f).AddRecord(new MovieType { Name = "Concert" }).Id;
        Movie heat = new MovieService(f).GetRecordByName("Heat (1995)")!;
        heat.MovieTypeId = concert;
        new MovieService(f).UpdateRecord(heat.Id, heat);
        int drama = new TVShowTypeService(f).AddRecord(new TVShowType { Name = "Drama" }).Id;
        var service = new MediaSearchService(f);

        // Browse: only the artists with a live album, and only that album below them.
        MediaSearchResult liveAlbums = service.Search(new MediaSearchCriteria { Text = "*", MusicRoots = [Music, Live], AlbumTypeIds = [live] });
        Assert.AreEqual("Queen", liveAlbums.Artists.Single().Name);
        Assert.AreEqual("Live Killers", service.GetArtistAlbums(lib.Queen.Id, [Music, Live], albumTypeIds: [live]).Single().Name);

        // A track only matches in an album of the type.
        Assert.IsEmpty(service.Search(new MediaSearchCriteria { Text = "bohemian", MusicRoots = [Music, Live], AlbumTypeIds = [live] }).MatchedTracks);
        Assert.HasCount(1, service.Search(new MediaSearchCriteria { Text = "waterloo", MusicRoots = [Music], AlbumTypeIds = [compilation] }).MatchedTracks);

        Assert.AreEqual("Heat (1995)", service.Search(new MediaSearchCriteria { Text = "*", MovieRoots = [Movies], MovieTypeIds = [concert] }).Movies.Single().Name);
        MediaSearchResult dramas = service.Search(new MediaSearchCriteria { Text = "serenity", TVRoots = [TV], TVShowTypeIds = [drama] });
        Assert.IsEmpty(dramas.Shows, "Firefly is of type 0, not Drama, so its episodes don't match either");
        Assert.IsEmpty(dramas.MatchedEpisodes);
    }

    [TestMethod]
    public void TestMaxResults()
    {
        using var db = new SqlServerTestDatabase();
        Library.Add(db);
        var service = new MediaSearchService(db.ContextFactory);

        MediaSearchResult result = service.Search(new MediaSearchCriteria { MusicRoots = [Music], MaxResults = 1 });

        Assert.AreEqual("ABBA", result.Artists.Single().Name);
        Assert.IsTrue(result.IsTruncated);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestSaveEditableFields(bool async)
    {
        using var db = new SqlServerTestDatabase();
        Library lib = Library.Add(db);
        var service = new MediaSearchService(db.ContextFactory);
        new AlbumService(db.ContextFactory).DeleteRecord(lib.Gold.Id);
        int live = new AlbumTypeService(db.ContextFactory).AddRecord(new AlbumType { Name = "Live" }).Id;

        // Edit a (deleted) album: the editable fields are saved, the rest is kept as stored.
        var edited = new Album
        {
            Id = lib.Gold.Id,
            Name = "Renamed",
            Folder = "elsewhere",
            TrackCount = 99,
            ArtistId = 0,
            Description = "Greatest hits",
            Notes = "Bought twice",
            IsFavourite = true,
            MusicBrainzId = "mb-1",
            AlbumTypeId = live,
            Length = 3600,
        };
        Album saved = async ? await service.SaveEditableFieldsAsync(edited) : service.SaveEditableFields(edited);

        Album stored = service.GetArtistAlbums(lib.Abba.Id, [], includeDeleted: true).Single(a => a.Id == lib.Gold.Id);
        Assert.AreEqual(("Gold", lib.Gold.Folder, 2, lib.Abba.Id), (stored.Name, stored.Folder, stored.TrackCount, stored.ArtistId), "scan-owned fields kept");
        Assert.AreEqual(("Greatest hits", "Bought twice", true, "mb-1", live, 3600), (stored.Description, stored.Notes, stored.IsFavourite, stored.MusicBrainzId, stored.AlbumTypeId, stored.Length));
        Assert.AreEqual("Gold", saved.Name);
        Assert.IsTrue(stored.IsDeleted, "still deleted");

        var episode = new TVShowEpisode { Id = lib.Serenity.Id, Name = "Renamed", EpisodeNo = 9, ReleaseDate = new DateTime(2002, 12, 20), Notes = "Pilot" };
        service.SaveEditableFields(episode);
        TVShowEpisode storedEpisode = service.GetShowEpisodes(lib.Firefly.Id).First();
        Assert.AreEqual(("Serenity", 1, new DateTime(2002, 12, 20), "Pilot"), (storedEpisode.Name, storedEpisode.EpisodeNo, storedEpisode.ReleaseDate, storedEpisode.Notes));

        Assert.ThrowsExactly<InvalidOperationException>(() => service.SaveEditableFields(new Movie { Id = 12345, Name = "Missing" }));
        Assert.ThrowsExactly<NotSupportedException>(() => MediaSearchService.CopyEditableFields(new MediaCategory(), new MediaCategory()));
    }

    private static Task<MediaSearchResult> Search(MediaSearchService service, bool async, MediaSearchCriteria criteria) =>
        async ? service.SearchAsync(criteria) : Task.FromResult(service.Search(criteria));

    /// <summary>A small library: ABBA and Queen in Music (Queen also in Live), two movies, one TV show (and Music2 next to Music).</summary>
    private sealed record Library(Artist Abba, Artist Queen, Album Gold, Album Opera, Track Waterloo, Movie Alien, TVShow Firefly, TVShowEpisode Serenity)
    {
        public static Library Add(SqlServerTestDatabase db)
        {
            var f = db.ContextFactory;
            var artists = new ArtistService(f);
            var albums = new AlbumService(f);
            var tracks = new TrackService(f);
            Artist abba = artists.AddRecord(new Artist { Name = "ABBA", Folder = Path.Combine(Music, "ABBA") });
            Artist queen = artists.AddRecord(new Artist { Name = "Queen", Folder = Path.Combine(Music, "Queen") });
            Artist other = artists.AddRecord(new Artist { Name = "Other", Folder = Path.Combine("nas", "Music2", "Other") });
            Album gold = albums.AddRecord(new Album { ArtistId = abba.Id, Name = "Gold", Folder = Path.Combine(Music, "ABBA", "Gold"), TrackCount = 2 });
            Album opera = albums.AddRecord(new Album
            {
                ArtistId = queen.Id,
                Name = "A Night at the Opera",
                Folder = Path.Combine(Music, "Queen", "A Night at the Opera"),
                Notes = "The vinyl pressing",
            });
            albums.AddRecord(new Album { ArtistId = queen.Id, Name = "Live Killers", Folder = Path.Combine(Live, "Queen", "Live Killers") });
            albums.AddRecord(new Album { ArtistId = other.Id, Name = "Elsewhere", Folder = Path.Combine("nas", "Music2", "Other", "Elsewhere") });
            tracks.AddRecord(new Track { AlbumId = gold.Id, ArtistId = abba.Id, TrackNo = 1, Name = "Dancing Queen", Folder = gold.Folder });
            Track waterloo = tracks.AddRecord(new Track { AlbumId = gold.Id, ArtistId = abba.Id, TrackNo = 2, Name = "Waterloo", Folder = gold.Folder });
            tracks.AddRecord(new Track { AlbumId = opera.Id, ArtistId = queen.Id, TrackNo = 11, Name = "Bohemian Rhapsody", Folder = opera.Folder });

            var movies = new MovieService(f);
            Movie alien = movies.AddRecord(new Movie { Name = "Alien", Folder = Path.Combine(Movies, "Raw Blu-Rays", "Alien") });
            movies.AddRecord(new Movie { Name = "Heat (1995)", Folder = Path.Combine(Movies, "Compressed Blu-Rays", "Heat (1995)"), Description = "Bought on vinyl? No, Blu-ray" });

            TVShow firefly = new TVShowService(f).AddRecord(new TVShow { Name = "Firefly", ReleaseYear = 2002, Folder = Path.Combine(TV, "Firefly (2002)"), IsFavourite = true });
            var episodes = new TVShowEpisodeService(f);
            string season1 = Path.Combine(firefly.Folder!, "Season 01");
            episodes.AddRecord(new TVShowEpisode { TVShowId = firefly.Id, SeasonNo = 2, EpisodeNo = 1, Name = "Pilot", Folder = Path.Combine(firefly.Folder!, "Season 02") });
            episodes.AddRecord(new TVShowEpisode { TVShowId = firefly.Id, SeasonNo = 1, EpisodeNo = 2, Name = "The Train Job", Folder = season1 });
            TVShowEpisode serenity = episodes.AddRecord(new TVShowEpisode { TVShowId = firefly.Id, SeasonNo = 1, EpisodeNo = 1, Name = "Serenity", Folder = season1 });
            return new Library(abba, queen, gold, opera, waterloo, alien, firefly, serenity);
        }
    }
}
