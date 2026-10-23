using System.IO;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;

namespace PDA.Media.Tests;

/// <summary>MediaAnalysisService against a real SQL Server database (see SqlServerTestDatabase; Inconclusive without one). No files are needed.</summary>
[TestClass]
public sealed class MediaAnalysisServiceTests
{
    private static readonly string MusicRoot = Path.Combine(Path.GetTempPath(), "Media", "Music");
    private static readonly string OtherMusicRoot = Path.Combine(Path.GetTempPath(), "Media", "Live Music");
    private static readonly string MovieRoot = Path.Combine(Path.GetTempPath(), "Media", "Movies");
    private static readonly string TVRoot = Path.Combine(Path.GetTempPath(), "Media", "TV");

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestStatsAndSummary(bool async)
    {
        using var db = new SqlServerTestDatabase();
        Seed(db);
        var service = new MediaAnalysisService(db.ContextFactory);
        var music = new MediaAnalysisScope(MediaAnalysisKind.Music, MusicRoot);

        MediaStats stats = async ? await service.GetStatsAsync(music) : service.GetStats(music);
        MediaCategorySummary summary = async ? await service.GetSummaryAsync(music) : service.GetSummary(music);

        Assert.AreEqual(4, stats.Items, "the live tracks under the root");
        Assert.AreEqual(3, stats.Folders);
        Assert.AreEqual(450L, stats.Size);
        Assert.AreEqual(1, stats.WithoutSize);
        Assert.AreEqual(150L, stats.AverageSize, "of the three files with a size");
        Assert.AreEqual(new DateTime(2020, 1, 1), stats.Oldest);
        Assert.AreEqual(new DateTime(2023, 1, 1), stats.Newest);
        Assert.AreEqual(2, summary.Groups, "artists");
        Assert.AreEqual(3, summary.SubGroups, "albums");
        Assert.AreEqual(1, summary.Deleted);
        Assert.AreEqual(5, service.GetStats(music with { IncludeDeleted = true }).Items);

        MediaCategorySummary tv = service.GetSummary(new MediaAnalysisScope(MediaAnalysisKind.TV, TVRoot));
        Assert.AreEqual((4, 1, 2), (tv.Stats.Items, tv.Groups, tv.SubGroups), "4 episodes, 1 show with episodes, 2 seasons");
        Assert.AreEqual(2, service.GetSummary(new MediaAnalysisScope(MediaAnalysisKind.Movies, MovieRoot)).Groups, "source folders");
        Assert.AreEqual(MediaStats.Empty, service.GetStats(new MediaAnalysisScope(MediaAnalysisKind.Music, OtherMusicRoot)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestMusicTree(bool async)
    {
        using var db = new SqlServerTestDatabase();
        Seed(db);
        var service = new MediaAnalysisService(db.ContextFactory);
        var root = new MediaAnalysisScope(MediaAnalysisKind.Music, MusicRoot);

        List<MediaAnalysisNode> artists = async ? await service.GetChildrenAsync(root) : service.GetChildren(root);

        CollectionAssert.AreEqual(new[] { "ABBA", "Björk" }, artists.Select(a => a.Name).ToArray(), "artists without tracks under the root aren't listed");
        Assert.AreEqual(MediaAnalysisLevel.Artist, artists[0].Scope.Level);
        Assert.AreEqual((3, 450L), (artists[0].Stats.Items, artists[0].Stats.Size));
        Assert.AreEqual(Path.Combine(MusicRoot, "ABBA"), artists[0].Folder);
        Assert.AreEqual(Path.Combine(MusicRoot, "Björk"), artists[1].Folder, "Björk's stored folder is in another category: the folder in this one");

        List<MediaAnalysisNode> albums = service.GetChildren(artists[0].Scope);
        CollectionAssert.AreEqual(new[] { "Arrival", "Gold" }, albums.Select(a => a.Name).ToArray());
        Assert.AreEqual(Path.Combine(MusicRoot, "ABBA", "Gold"), albums[1].Folder);

        List<MediaAnalysisNode> tracks = service.GetChildren(albums[1].Scope);
        CollectionAssert.AreEqual(new[] { "01 - Dancing Queen", "02 - Waterloo" }, tracks.Select(t => t.Name).ToArray());
        Assert.IsTrue(tracks[0].IsLeaf);
        Assert.AreEqual("01 - Dancing Queen.flac", tracks[0].FileName);
        Assert.AreEqual(100L, tracks[0].Stats.Size);
        Assert.IsEmpty(service.GetChildren(tracks[0].Scope));
        Assert.AreEqual(100L, service.GetStats(tracks[0].Scope).Size);

        CollectionAssert.AreEqual(new[] { "01 - Dancing Queen", "02 - Waterloo", "03 - Gone" },
            service.GetChildren(albums[1].Scope with { IncludeDeleted = true }).Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public void TestMovieAndTVTrees()
    {
        using var db = new SqlServerTestDatabase();
        Seed(db);
        var service = new MediaAnalysisService(db.ContextFactory);

        List<MediaAnalysisNode> sources = service.GetChildren(new MediaAnalysisScope(MediaAnalysisKind.Movies, MovieRoot));
        CollectionAssert.AreEqual(new[] { "Compressed", "Raw Blu-Rays", "Root Movie" }, sources.Select(s => s.Name).ToArray(), "a movie straight in the root comes last");
        Assert.AreEqual((2, 5700L), (sources[1].Stats.Items, sources[1].Stats.Size));
        Assert.IsTrue(sources[2].IsLeaf);
        CollectionAssert.AreEqual(new[] { "Alien (1979)", "Loose" }, service.GetChildren(sources[1].Scope).Select(m => m.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Heat (1995)" }, service.GetChildren(sources[0].Scope).Select(m => m.Name).ToArray(), "movies in a group folder too");

        List<MediaAnalysisNode> shows = service.GetChildren(new MediaAnalysisScope(MediaAnalysisKind.TV, TVRoot));
        CollectionAssert.AreEqual(new[] { "Firefly (2002)" }, shows.Select(s => s.Name).ToArray());
        List<MediaAnalysisNode> seasons = service.GetChildren(shows[0].Scope);
        CollectionAssert.AreEqual(new[] { "Specials", "Season 1" }, seasons.Select(s => s.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "s01e01 - Serenity", "s01e02 - The Train Job", "s01e04 - Shindig" },
            service.GetChildren(seasons[1].Scope).Select(e => e.Name).ToArray());
        Assert.AreEqual(3000L, seasons[1].Stats.Size);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestLargestAndFileTypes(bool async)
    {
        using var db = new SqlServerTestDatabase();
        Seed(db);
        var service = new MediaAnalysisService(db.ContextFactory);
        var music = new MediaAnalysisScope(MediaAnalysisKind.Music, MusicRoot);

        List<MediaAnalysisItem> files = async ? await service.GetLargestAsync(music, 2) : service.GetLargest(music, 2);
        List<MediaAnalysisItem> albums = service.GetLargest(music, 20, MediaLargestBy.Groups);
        List<MediaFileType> types = async ? await service.GetFileTypesAsync(music) : service.GetFileTypes(music);

        CollectionAssert.AreEqual(new[] { "02 - Waterloo", "01 - Dancing Queen" }, files.Select(f => f.Name).ToArray());
        Assert.AreEqual("ABBA · Gold", files[0].Parent);
        Assert.AreEqual(Path.Combine(MusicRoot, "ABBA", "Gold", "02 - Waterloo.flac"), files[0].Path);
        Assert.AreEqual(300L, files[0].Size);
        Assert.AreEqual(albums[0].Scope.AlbumId, files[0].Scope.AlbumId, "a file keeps the way down to it");
        Assert.AreEqual(MediaAnalysisLevel.Item, files[0].Scope.Level);

        CollectionAssert.AreEqual(new[] { "Gold", "Arrival" }, albums.Select(a => a.Name).ToArray(), "albums whose files have no size aren't ranked");
        Assert.AreEqual((400L, 2, "ABBA"), (albums[0].Size!.Value, albums[0].Items, albums[0].Parent));
        Assert.AreEqual(MediaAnalysisLevel.Album, albums[0].Scope.Level);

        CollectionAssert.AreEqual(new[] { new MediaFileType(".flac", 3, 400), new MediaFileType(".mp3", 1, 50) }, types);

        var tv = new MediaAnalysisScope(MediaAnalysisKind.TV, TVRoot);
        Assert.AreEqual(("Firefly (2002)", 3200L), (service.GetLargest(tv, 5, MediaLargestBy.Groups).Single().Name, service.GetLargest(tv, 5, MediaLargestBy.Groups)[0].Size));
        Assert.AreEqual("Firefly (2002) · Season 1", service.GetLargest(tv, 1)[0].Parent);
        Assert.AreEqual("Alien (1979)", service.GetLargest(new MediaAnalysisScope(MediaAnalysisKind.Movies, MovieRoot), 1, MediaLargestBy.Groups)[0].Name,
            "movies are ranked by file");
        Assert.HasCount(5, service.GetFiles(music with { IncludeDeleted = true }));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestChecks(bool async)
    {
        using var db = new SqlServerTestDatabase();
        Seed(db);
        var service = new MediaAnalysisService(db.ContextFactory);

        Dictionary<string, MediaCheck> music = (async ? await service.GetChecksAsync(new MediaAnalysisScope(MediaAnalysisKind.Music, MusicRoot))
            : service.GetChecks(new MediaAnalysisScope(MediaAnalysisKind.Music, MusicRoot))).ToDictionary(c => c.Title);
        Dictionary<string, MediaCheck> movies = service.GetChecks(new MediaAnalysisScope(MediaAnalysisKind.Movies, MovieRoot)).ToDictionary(c => c.Title);
        Dictionary<string, MediaCheck> tv = service.GetChecks(new MediaAnalysisScope(MediaAnalysisKind.TV, TVRoot)).ToDictionary(c => c.Title);

        CollectionAssert.AreEquivalent(new[] { "Arrival", "Homogenic" }, music["Albums without cover art"].Rows.Select(r => r.Name).ToArray());
        MediaCheck wrongCount = music["Albums whose track count is wrong"];
        Assert.AreEqual(1, wrongCount.Count);
        Assert.AreEqual(("Arrival", "3 stored, 1 found"), (wrongCount.Rows[0].Name, wrongCount.Rows[0].Note));
        Assert.AreEqual("Lonely", music["Artists without albums"].Rows.Single().Name);
        Assert.AreEqual("01 - Jóga", music["Files without a size"].Rows.Single().Name);
        Assert.AreEqual("03 - Gone", music["Deleted files still in the database"].Rows.Single().Name);
        Assert.IsTrue(music["Deleted files still in the database"].Rows.Single().IsDeleted);

        Assert.AreEqual(3, movies["Movies without cover art"].Count);
        CollectionAssert.AreEquivalent(new[] { "Loose", "Root Movie" }, movies["Movies without their own folder"].Rows.Select(r => r.Name).ToArray());

        MediaCheck gaps = tv["Seasons with missing episodes"];
        Assert.AreEqual(("Season 1", "Firefly (2002)", "missing 3"), (gaps.Rows.Single().Name, gaps.Rows.Single().Parent, gaps.Rows.Single().Note));
        Assert.AreEqual("Making Of", tv["Episodes without a number"].Rows.Single().Name);
        Assert.AreEqual("Empty (2020)", tv["Shows without episodes"].Rows.Single().Name);
        Assert.AreEqual(2, tv["Shows without cover art"].Count);

        // Narrowed to an album, only its own problems are found.
        Album goldAlbum = new AlbumService(db.ContextFactory).GetAllRecords("Gold").Single();
        Dictionary<string, MediaCheck> gold = service.GetChecks(new MediaAnalysisScope(MediaAnalysisKind.Music, MusicRoot) { ArtistId = goldAlbum.ArtistId, AlbumId = goldAlbum.Id })
            .ToDictionary(c => c.Title);
        Assert.AreEqual(0, gold["Albums whose track count is wrong"].Count);
        Assert.AreEqual(0, gold["Albums without cover art"].Count);
        Assert.IsFalse(gold.ContainsKey("Artists without albums"));
    }

    [TestMethod]
    [DataRow(@"C:\Movies", @"C:\Movies\Raw\Alien (1979)", @"C:\Movies\Raw")]
    [DataRow(@"C:\Movies\", @"C:\Movies\Raw", @"C:\Movies\Raw")]
    [DataRow("/m/Movies", "/m/Movies/Raw/Group/Heat", "/m/Movies/Raw")]
    [DataRow(@"C:\Movies", @"C:\Movies", null)]
    [DataRow(@"C:\Movies", @"C:\Movies Old\Raw", null)]
    [DataRow(@"C:\Movies", null, null)]
    public void TestSourceFolderOf(string root, string? folder, string? expected) =>
        Assert.AreEqual(expected, MediaAnalysisService.SourceFolderOf(root, folder));

    [TestMethod]
    public void TestNamesAndExtensions()
    {
        Assert.AreEqual("3, 5-7, 10", MediaAnalysisService.NumberRanges([7, 3, 5, 6, 10]));
        Assert.AreEqual(".mkv", MediaAnalysisService.ExtensionOf("Alien (1979).MKV"));
        Assert.AreEqual("(none)", MediaAnalysisService.ExtensionOf("README"));
        Assert.AreEqual("(none)", MediaAnalysisService.ExtensionOf(null));
        Assert.AreEqual("Firefly (2002)", MediaAnalysisService.ShowName(new TVShow { Name = "Firefly", ReleaseYear = 2002 }));
        Assert.AreEqual("Firefly (2002)", MediaAnalysisService.ShowName(new TVShow { Name = "Firefly (2002)", ReleaseYear = 2002 }));
        Assert.AreEqual("Specials", MediaAnalysisService.SeasonName(0));
    }

    /// <summary>
    /// Music: ABBA (Gold: 2 tracks + a deleted one, cover; Arrival: 1 track, track count 3), Björk (folder in another category; Homogenic: 1 track without a
    /// size), Lonely (no albums). Movies: Raw Blu-Rays/Alien (1979) (cover), Raw Blu-Rays/Loose.mkv, Compressed/Group/Heat (1995), Root Movie.mkv.
    /// TV: Firefly (2002) (s01e01, e02, e04 and an unnumbered special), Empty (2020) (no episodes).
    /// </summary>
    private static void Seed(SqlServerTestDatabase db)
    {
        var artists = new ArtistService(db.ContextFactory);
        var albums = new AlbumService(db.ContextFactory);
        var tracks = new TrackService(db.ContextFactory);
        Artist abba = artists.AddRecord(new Artist { Name = "ABBA", Folder = Path.Combine(MusicRoot, "ABBA") });
        Artist bjork = artists.AddRecord(new Artist { Name = "Björk", Folder = Path.Combine(OtherMusicRoot, "Björk") });
        artists.AddRecord(new Artist { Name = "Lonely", Folder = Path.Combine(MusicRoot, "Lonely") });
        Album gold = albums.AddRecord(new Album { ArtistId = abba.Id, Name = "Gold", Folder = Path.Combine(MusicRoot, "ABBA", "Gold"), TrackCount = 2, CoverArtFilename = "cover.jpg" });
        Album arrival = albums.AddRecord(new Album { ArtistId = abba.Id, Name = "Arrival", Folder = Path.Combine(MusicRoot, "ABBA", "Arrival"), TrackCount = 3 });
        Album homogenic = albums.AddRecord(new Album { ArtistId = bjork.Id, Name = "Homogenic", Folder = Path.Combine(MusicRoot, "Björk", "Homogenic"), TrackCount = 1 });
        AddTrack(gold, 1, "Dancing Queen", ".flac", 100, new DateTime(2020, 1, 1));
        AddTrack(gold, 2, "Waterloo", ".flac", 300, new DateTime(2021, 1, 1));
        tracks.DeleteRecord(AddTrack(gold, 3, "Gone", ".flac", 999, null).Id);
        AddTrack(arrival, 1, "When I Kissed the Teacher", ".mp3", 50, new DateTime(2023, 1, 1));
        AddTrack(homogenic, 1, "Jóga", ".flac", null, null);

        var movies = new MovieService(db.ContextFactory);
        AddMovie("Alien (1979)", Path.Combine(MovieRoot, "Raw Blu-Rays", "Alien (1979)"), "Alien (1979).mkv", 5000, "poster.jpg");
        AddMovie("Loose", Path.Combine(MovieRoot, "Raw Blu-Rays"), "Loose.mkv", 700, null);
        AddMovie("Heat (1995)", Path.Combine(MovieRoot, "Compressed", "Group", "Heat (1995)"), "Heat.mp4", 2000, null);
        AddMovie("Root Movie", MovieRoot, "Root Movie.mkv", 10, null);

        var shows = new TVShowService(db.ContextFactory);
        var episodes = new TVShowEpisodeService(db.ContextFactory);
        TVShow firefly = shows.AddRecord(new TVShow { Name = "Firefly", ReleaseYear = 2002, Folder = Path.Combine(TVRoot, "Firefly (2002)") });
        shows.AddRecord(new TVShow { Name = "Empty", ReleaseYear = 2020, Folder = Path.Combine(TVRoot, "Empty (2020)") });
        AddEpisode(1, 1, "Serenity", 1000);
        AddEpisode(1, 2, "The Train Job", 1100);
        AddEpisode(1, 4, "Shindig", 900);
        AddEpisode(0, 0, "Making Of", 200);
        return;

        Track AddTrack(Album album, int number, string name, string extension, long? size, DateTime? date) =>
            tracks.AddRecord(new Track
            {
                AlbumId = album.Id,
                ArtistId = album.ArtistId,
                TrackNo = number,
                Name = name,
                Folder = album.Folder,
                OriginalFilename = $"{number:00} - {name}{extension}",
                FileSize = size,
                FileDate = date,
            });

        void AddMovie(string name, string folder, string file, long size, string? cover) =>
            movies.AddRecord(new Movie { Name = name, Folder = folder, OriginalFilename = file, FileSize = size, CoverArtFilename = cover });

        void AddEpisode(int season, int number, string name, long size) =>
            episodes.AddRecord(new TVShowEpisode
            {
                TVShowId = firefly.Id,
                SeasonNo = season,
                EpisodeNo = number,
                Name = name,
                Folder = Path.Combine(TVRoot, "Firefly (2002)", season == 0 ? "Specials" : $"Season {season:00}"),
                OriginalFilename = $"{name}.mkv",
                FileSize = size,
            });
    }
}
