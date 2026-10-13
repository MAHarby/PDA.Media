using System.IO;
using Microsoft.EntityFrameworkCore;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;

namespace PDA.Media.Tests;

/// <summary>
/// MediaRenameService against folder trees in a temp folder scanned into SqlServerTestDatabase (Inconclusive without one).
/// After each rename a scan must find the same rows: nothing added or deleted, same ids.
/// </summary>
[TestClass]
public sealed class MediaRenameServiceTests
{
    [TestMethod]
    public async Task TestMovieFolderAndFiles()
    {
        using var lib = new Library();
        lib.File("Movies", "Compressed Blu-Rays", "Heat 1995", "Heat 1995.mkv", size: 100);
        lib.File("Movies", "Compressed Blu-Rays", "Heat 1995", "Heat 1995 - Sample.mkv");
        lib.File("Movies", "Compressed Blu-Rays", "Heat 1995", "Heat 1995.en.srt");
        lib.File("Movies", "Compressed Blu-Rays", "Heat 1995", "poster.jpg");
        lib.File("Movies", "Compressed Blu-Rays", "Heat 1995 Extras", "Heat 1995 Extras.mkv");
        await lib.ScanAsync();
        var movies = new MovieService(lib.Db.ContextFactory);
        Movie heat = movies.GetRecordByName("Heat 1995")!;
        heat.Notes = "Director's cut";
        movies.UpdateRecord(heat.Id, heat);

        MediaRenamePlan plan = await lib.Renamer().PlanAsync(new MediaRenameRequest(heat, "Heat (1995)"));
        Assert.IsTrue(plan.CanApply, string.Join("\n", plan.Problems));
        CollectionAssert.AreEquivalent(
            new[] { "File: Heat 1995.mkv → Heat (1995).mkv", "File: Heat 1995 - Sample.mkv → Heat (1995) - Sample.mkv", "File: Heat 1995.en.srt → Heat (1995).en.srt",
                "Folder: Heat 1995 → Heat (1995)" },
            plan.Steps.Select(s => s.ToString()).ToArray(), "not poster.jpg, nor the 'Heat 1995 Extras' folder next to it");
        Assert.AreEqual(MediaRenameStepKind.Folder, plan.Steps.Last().Kind, "files first, then the folder");
        StringAssert.Contains(plan.Warnings.Single(), "Plex");

        MediaRenameResult result = await lib.Renamer().ApplyAsync(plan);

        Assert.IsTrue(result.Succeeded, result.Message);
        string folder = lib.Path("Movies", "Compressed Blu-Rays", "Heat (1995)");
        CollectionAssert.AreEquivalent(new[] { "Heat (1995).mkv", "Heat (1995) - Sample.mkv", "Heat (1995).en.srt", "poster.jpg" },
            Directory.GetFiles(folder).Select(Path.GetFileName).ToArray());
        Movie renamed = movies.GetRecordById(heat.Id)!;
        Assert.AreEqual(("Heat (1995)", folder, "Heat (1995).mkv", "Director's cut"), (renamed.Name, renamed.Folder, renamed.OriginalFilename, renamed.Notes));
        await lib.AssertRescanChangesNothingAsync();
        Assert.AreEqual(heat.Id, movies.GetRecordByName("Heat (1995)")!.Id);
    }

    [TestMethod]
    public async Task TestMovieFolderOnlyFilesOnlyAndLooseFiles()
    {
        using var lib = new Library();
        lib.File("Movies", "Raw Blu-Rays", "Alien", "Title_t00.mkv");
        lib.File("Movies", "Raw Blu-Rays", "Ronin.mkv");
        lib.File("Movies", "Raw Blu-Rays", "Ronin.srt");
        await lib.ScanAsync();
        var movies = new MovieService(lib.Db.ContextFactory);
        Movie alien = movies.GetRecordByName("Alien")!, ronin = movies.GetRecordByName("Ronin")!;

        // Files only: the main file takes the new name, the movie keeps its folder's name.
        MediaRenamePlan filesOnly = await lib.Renamer().PlanAsync(new MediaRenameRequest(alien, "Alien (1979)", RenameFolder: false));
        Assert.AreEqual("File: Title_t00.mkv → Alien (1979).mkv", filesOnly.Steps.Single().ToString());
        Assert.AreEqual("Alien", filesOnly.NewDatabaseName);
        Assert.IsTrue(filesOnly.Warnings.Any(w => w.Contains("comes from its folder")));
        Assert.IsTrue((await lib.Renamer().ApplyAsync(filesOnly)).Succeeded);
        Assert.AreEqual("Alien (1979).mkv", movies.GetRecordById(alien.Id)!.OriginalFilename);

        // Folder only.
        MediaRenamePlan folderOnly = await lib.Renamer().PlanAsync(new MediaRenameRequest(alien, "Alien (1979)", RenameFiles: false));
        Assert.AreEqual("Folder: Alien → Alien (1979)", folderOnly.Steps.Single().ToString());
        Assert.IsTrue((await lib.Renamer().ApplyAsync(folderOnly)).Succeeded);

        // A loose movie is renamed by its files.
        Assert.IsFalse((await lib.Renamer().PlanAsync(new MediaRenameRequest(ronin, "Ronin (1998)", RenameFiles: false))).CanApply);
        MediaRenamePlan loose = await lib.Renamer().PlanAsync(new MediaRenameRequest(ronin, "Ronin (1998)"));
        CollectionAssert.AreEquivalent(new[] { "File: Ronin.mkv → Ronin (1998).mkv", "File: Ronin.srt → Ronin (1998).srt" }, loose.Steps.Select(s => s.ToString()).ToArray());
        Assert.IsTrue((await lib.Renamer().ApplyAsync(loose)).Succeeded);
        Assert.AreEqual("Ronin (1998)", movies.GetRecordById(ronin.Id)!.Name);

        await lib.AssertRescanChangesNothingAsync();
    }

    [TestMethod]
    public async Task TestTVShowAndEpisode()
    {
        using var lib = new Library();
        lib.File("TV", "Firefly", "Season 01", "Firefly - s01e01 - Serenity.mkv");
        lib.File("TV", "Firefly", "Season 01", "Firefly - s01e01 - Serenity.en.srt");
        lib.File("TV", "Firefly", "Season 01", "Firefly - s01e02 - The Train Job.mkv");
        lib.File("TV", "Firefly", "Specials", "Behind the scenes.mkv");
        await lib.ScanAsync();
        var shows = new TVShowService(lib.Db.ContextFactory);
        var episodes = new TVShowEpisodeService(lib.Db.ContextFactory);
        TVShow firefly = shows.GetAllRecords().Single();
        int serenityId = episodes.GetAllRecords("Serenity").Single().Id;

        MediaRenamePlan show = await lib.Renamer().PlanAsync(new MediaRenameRequest(firefly, "Firefly (2002)"));
        Assert.IsTrue(show.CanApply, string.Join("\n", show.Problems));
        Assert.HasCount(4, show.Steps, "3 episode files and the folder; the special without a number keeps its name");
        Assert.IsTrue(show.Warnings.Any(w => w.Contains("1 video file(s) without an episode number")));
        Assert.IsTrue((await lib.Renamer().ApplyAsync(show)).Succeeded);

        TVShow renamed = shows.GetRecordById(firefly.Id)!;
        Assert.AreEqual(("Firefly", 2002, lib.Path("TV", "Firefly (2002)")), (renamed.Name, renamed.ReleaseYear, renamed.Folder));
        TVShowEpisode serenity = episodes.GetRecordById(serenityId)!;
        Assert.AreEqual(("Firefly (2002) - s01e01 - Serenity.mkv", lib.Path("TV", "Firefly (2002)", "Season 01")), (serenity.OriginalFilename, serenity.Folder));
        await lib.AssertRescanChangesNothingAsync();

        // An episode's title: its file (and subtitles) keep the show and number.
        MediaRenamePlan episode = await lib.Renamer().PlanAsync(new MediaRenameRequest(serenity, "Serenity (Pilot)"));
        CollectionAssert.AreEquivalent(new[] { "File: Firefly (2002) - s01e01 - Serenity.mkv → Firefly (2002) - s01e01 - Serenity (Pilot).mkv",
            "File: Firefly (2002) - s01e01 - Serenity.en.srt → Firefly (2002) - s01e01 - Serenity (Pilot).en.srt" }, episode.Steps.Select(s => s.ToString()).ToArray());
        Assert.IsTrue((await lib.Renamer().ApplyAsync(episode)).Succeeded);
        Assert.AreEqual("Serenity (Pilot)", episodes.GetRecordById(serenityId)!.Name);
        await lib.AssertRescanChangesNothingAsync();
    }

    [TestMethod]
    public async Task TestTrackAlbumAndArtistInTwoCategories()
    {
        using var lib = new Library();
        lib.File("Music", "Queen", "A Night at the Opera [FLAC]", "11 - Bohemian Rhapsody.flac");
        lib.File("Live", "Queen", "Live Killers", "01 - We Will Rock You.flac");
        await lib.ScanAsync();
        var albums = new AlbumService(lib.Db.ContextFactory);
        var tracks = new TrackService(lib.Db.ContextFactory);
        Album opera = albums.GetAllRecords("A Night at the Opera*").Single();
        Track rhapsody = tracks.GetAllRecords("Bohemian*").Single();
        Artist queen = new ArtistService(lib.Db.ContextFactory).GetRecordByName("Queen")!;

        MediaRenamePlan track = await lib.Renamer().PlanAsync(new MediaRenameRequest(rhapsody, "Bohemian Rhapsody (Remastered)"));
        Assert.AreEqual("File: 11 - Bohemian Rhapsody.flac → 11 - Bohemian Rhapsody (Remastered).flac", track.Steps.Single().ToString());
        Assert.IsTrue((await lib.Renamer().ApplyAsync(track)).Succeeded);

        MediaRenamePlan album = await lib.Renamer().PlanAsync(new MediaRenameRequest(opera, "A Night at the Opera"));
        Assert.IsTrue((await lib.Renamer().ApplyAsync(album)).Succeeded);
        Track movedTrack = tracks.GetRecordById(rhapsody.Id)!;
        Assert.AreEqual(lib.Path("Music", "Queen", "A Night at the Opera"), movedTrack.Folder, "the album's tracks move with it");

        MediaRenamePlan artist = await lib.Renamer().PlanAsync(new MediaRenameRequest(queen, "Queen (UK)"));
        Assert.HasCount(2, artist.Steps, "its folder in Music and in Live");
        Assert.IsTrue(artist.Warnings.Any(w => w.Contains("2 music categories")));
        Assert.IsTrue((await lib.Renamer().ApplyAsync(artist)).Succeeded);
        Assert.IsTrue(Directory.Exists(lib.Path("Live", "Queen (UK)", "Live Killers")));
        Assert.AreEqual(lib.Path("Music", "Queen (UK)", "A Night at the Opera"), tracks.GetRecordById(rhapsody.Id)!.Folder);

        await lib.AssertRescanChangesNothingAsync();
        Assert.AreEqual(queen.Id, new ArtistService(lib.Db.ContextFactory).GetRecordByName("Queen (UK)")!.Id);
    }

    [TestMethod]
    public async Task TestProblemsStopTheRename()
    {
        using var lib = new Library();
        lib.File("Movies", "Raw", "Heat", "Heat.mkv");
        lib.File("Movies", "Raw", "Alien", "Alien.mkv");
        lib.File("Movies", "Raw", "Taken", "readme.txt");
        await lib.ScanAsync();
        Movie heat = new MovieService(lib.Db.ContextFactory).GetRecordByName("Heat")!;
        MediaRenameService renamer = lib.Renamer();

        StringAssert.Contains((await renamer.PlanAsync(new MediaRenameRequest(heat, "Heat: Part 1"))).Problems.Single(), "can't contain");
        StringAssert.Contains((await renamer.PlanAsync(new MediaRenameRequest(heat, "Heat."))).Problems.Single(), "dot or a space");
        StringAssert.Contains((await renamer.PlanAsync(new MediaRenameRequest(heat, "CON"))).Problems.Single(), "doesn't allow");
        StringAssert.Contains((await renamer.PlanAsync(new MediaRenameRequest(heat, "Heat"))).Problems.Single(), "current name");
        Assert.IsTrue((await renamer.PlanAsync(new MediaRenameRequest(heat, "Alien"))).Problems.Any(p => p.Contains("already called Alien")));
        Assert.IsTrue((await renamer.PlanAsync(new MediaRenameRequest(heat, "Taken"))).Problems.Any(p => p.Contains("already exists")));
        Assert.IsFalse((await renamer.PlanAsync(new MediaRenameRequest(heat, "Heat (1995)", RenameFolder: false, RenameFiles: false))).CanApply);

        // A change of case only is allowed.
        MediaRenamePlan caseOnly = await renamer.PlanAsync(new MediaRenameRequest(heat, "HEAT"));
        Assert.IsTrue(caseOnly.CanApply, string.Join("\n", caseOnly.Problems));
        Assert.IsTrue((await renamer.ApplyAsync(caseOnly)).Succeeded);
        Assert.AreEqual("HEAT", Path.GetFileName(Directory.GetDirectories(lib.Path("Movies", "Raw")).Single(d => Path.GetFileName(d).Equals("heat", StringComparison.OrdinalIgnoreCase))));
    }

    [TestMethod]
    public async Task TestFailuresPutTheDiskBack()
    {
        using var lib = new Library();
        lib.File("Movies", "Raw", "Heat", "Heat.mkv");
        lib.File("Movies", "Raw", "Heat", "Heat.srt");
        await lib.ScanAsync();
        Movie heat = new MovieService(lib.Db.ContextFactory).GetRecordByName("Heat")!;
        string[] before = Directory.GetFileSystemEntries(lib.Path("Movies"), "*", SearchOption.AllDirectories).Order().ToArray();

        // A disk step fails part way: the steps done are put back.
        MediaRenameService renamer = lib.Renamer();
        int steps = 0;
        renamer.BeforeStep = _ =>
        {
            if (++steps == 3) throw new IOException("The file is in use");
        };
        MediaRenamePlan plan = await renamer.PlanAsync(new MediaRenameRequest(heat, "Heat (1995)"));
        MediaRenameResult failed = await renamer.ApplyAsync(plan);
        Assert.IsFalse(failed.Succeeded);
        StringAssert.Contains(failed.Message, "put back");
        CollectionAssert.AreEqual(before, Directory.GetFileSystemEntries(lib.Path("Movies"), "*", SearchOption.AllDirectories).Order().ToArray());
        Assert.AreEqual("Heat", new MovieService(lib.Db.ContextFactory).GetRecordById(heat.Id)!.Name);

        // The database update fails (the row has gone): the disk is put back too.
        MediaRenamePlan plan2 = await lib.Renamer().PlanAsync(new MediaRenameRequest(heat, "Heat (1995)"));
        using (var context = lib.Db.ContextFactory.CreateDbContext()) context.Database.ExecuteSqlRaw("DELETE FROM Movies WHERE Id = {0}", heat.Id);
        MediaRenameResult dbFailed = await lib.Renamer().ApplyAsync(plan2);
        Assert.IsFalse(dbFailed.Succeeded);
        StringAssert.Contains(dbFailed.Message, "database update failed");
        CollectionAssert.AreEqual(before, Directory.GetFileSystemEntries(lib.Path("Movies"), "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [TestMethod]
    public async Task TestNoRenameDuringAScan()
    {
        using var lib = new Library();
        lib.File("Movies", "Raw", "Heat", "Heat.mkv");
        await lib.ScanAsync();
        Movie heat = new MovieService(lib.Db.ContextFactory).GetRecordByName("Heat")!;
        MediaScanner scanner = lib.Scanner;
        MediaRenameService renamer = lib.Renamer();
        MediaRenamePlan plan = await renamer.PlanAsync(new MediaRenameRequest(heat, "Heat (1995)"));

        using (scanner.TryReserve())
        {
            MediaRenameResult result = await renamer.ApplyAsync(plan);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "scan");
            Assert.ThrowsExactly<InvalidOperationException>(() => scanner.ScanAllMediaCategories(), "and no scan during a rename");
        }
        Assert.IsTrue((await renamer.ApplyAsync(plan)).Succeeded);
    }

    [TestMethod]
    [DataRow("Heat (1995).mkv", "Heat (1995)", "Heat (1995) - Remastered.mkv")]
    [DataRow("Heat (1995) - Sample.mkv", "Heat (1995)", "Heat (1995) - Remastered - Sample.mkv")]
    [DataRow("heat (1995).srt", "Heat (1995)", "Heat (1995) - Remastered.srt")]
    [DataRow("Heat (1995) 2.mkv", "Heat (1995)", "Heat (1995) - Remastered 2.mkv")]
    [DataRow("Heat (1995)2.mkv", "Heat (1995)", null)]
    [DataRow("Heatwave.mkv", "Heat", null)]
    public void TestRenamePrefixed(string file, string oldStart, string? expected)
    {
        string? renamed = MediaRenameService.RenamePrefixed(file, oldStart, oldStart.Length > 4 ? "Heat (1995) - Remastered" : "Heat (1995)");
        Assert.AreEqual(expected, renamed);
    }

    /// <summary>Music (Music and Live), Movies and TV categories over temp folders, scanned into a test database.</summary>
    private sealed class Library : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pda_rename_test_" + Guid.NewGuid().ToString("N"));

        public Library()
        {
            var categories = new MediaCategoryService(Db.ContextFactory);
            foreach ((string name, string mediaClass) in new[] { ("Music", "Music"), ("Live", "Music"), ("Movies", "Movie"), ("TV", "TV") })
            {
                Directory.CreateDirectory(Path(name));
                categories.AddRecord(new MediaCategory { Name = name, MediaClass = mediaClass, RootFolder = Path(name) });
            }
            var f = Db.ContextFactory;
            Scanner = new MediaScanner(new MediaCategoryService(f), new ArtistService(f), new AlbumService(f), new AlbumTypeService(f), new TrackService(f),
                new MovieService(f), new TVShowService(f), new TVShowEpisodeService(f), new MediaCacheService(f), new SettingService(f));
        }

        public SqlServerTestDatabase Db { get; } = new();
        public MediaScanner Scanner { get; }

        public string Path(params string[] parts) => System.IO.Path.Combine([_root, .. parts]);

        public void File(params string[] parts) => Create(parts, 1);

        public void File(string a, string b, string c, string d, int size) => Create([a, b, c, d], size);

        private void Create(string[] parts, int size)
        {
            string path = Path(parts);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, new byte[size]);
        }

        public MediaRenameService Renamer()
        {
            var f = Db.ContextFactory;
            return new MediaRenameService(Scanner, new MediaPathService(f), new ArtistService(f), new AlbumService(f), new TrackService(f), new MovieService(f),
                new TVShowService(f), new TVShowEpisodeService(f));
        }

        public Task<MediaScanResult> ScanAsync() => Scanner.ScanAllMediaCategoriesAsync();

        public async Task AssertRescanChangesNothingAsync()
        {
            MediaScanResult result = await ScanAsync();
            Assert.AreEqual((0, 0, 0, 0), (result.Added, result.Restored, result.Updated, result.Deleted), "the scan finds the renamed rows as they are stored");
        }

        public void Dispose()
        {
            Db.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
