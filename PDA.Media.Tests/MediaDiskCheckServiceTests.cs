using System.IO;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;

namespace PDA.Media.Tests;

/// <summary>MediaDiskCheckService: folders in a temp folder, scanned into SqlServerTestDatabase (Inconclusive without one), then changed on disk.</summary>
[TestClass]
public sealed class MediaDiskCheckServiceTests
{
    [TestMethod]
    public async Task TestCheckFindsWhatChangedSinceTheScan()
    {
        using var db = new SqlServerTestDatabase();
        using var disk = new TempFolder();
        string movies = disk.Combine("Movies");
        disk.WriteFile(5000, "Movies", "Raw", "Alien (1979)", "Alien (1979).mkv");
        disk.WriteFile(20, "Movies", "Raw", "Alien (1979)", "poster.jpg");
        disk.WriteFile(700, "Movies", "Raw", "Loose.mkv");
        disk.WriteFile(900, "Movies", "Raw", "Heat (1995)", "Heat (1995).mkv");
        new MediaCategoryService(db.ContextFactory).AddRecord(new MediaCategory { Name = "Movies", MediaClass = "Movie", RootFolder = movies });
        Scanner(db).ScanAllMediaCategories();

        disk.WriteFile(80, "Movies", "Raw", "Alien (1979)", "Sample.mkv");     // an extra
        disk.WriteFile(6000, "Movies", "Raw", "Alien (1979)", "Alien (1979).mkv"); // re-encoded
        disk.WriteFile(300, "Movies", "Raw", "New Loose.mkv");                  // not scanned yet
        File.Delete(disk.Combine("Movies", "Raw", "Heat (1995)", "Heat (1995).mkv"));
        Directory.CreateDirectory(disk.Combine("Movies", "Raw", "Old", "Older"));
        var folders = new List<int>();

        MediaDiskCheckResult result = await new MediaDiskCheckService(new MediaAnalysisService(db.ContextFactory))
            .CheckAsync(MediaAnalysisKind.Movies, movies, new SyncProgress<int>(folders.Add));

        Assert.IsTrue(result.FolderFound);
        Assert.AreEqual(5, result.Files);
        Assert.AreEqual(6000L + 20 + 700 + 80 + 300, result.Size);
        Assert.AreEqual((4, 6000L + 700 + 80 + 300), (result.MediaFiles, result.MediaSize));
        Assert.AreEqual((3, 5000L + 700 + 900), (result.StoredFiles, result.StoredSize));
        Assert.AreEqual(6, result.Folders, "Movies, Raw, Alien, Heat, Old, Older");
        Assert.AreEqual(6, folders.Last());
        string Issue(MediaDiskIssueKind kind) => Path.GetFileName(result.Issues.Single(i => i.Kind == kind).Path);
        Assert.AreEqual("Sample.mkv", Issue(MediaDiskIssueKind.Extra));
        Assert.AreEqual("Alien (1979).mkv", Issue(MediaDiskIssueKind.Changed));
        Assert.AreEqual("New Loose.mkv", Issue(MediaDiskIssueKind.NotInDatabase), "a new file beside a loose movie isn't an extra");
        Assert.AreEqual("Heat (1995).mkv", Issue(MediaDiskIssueKind.MissingFromDisk));
        CollectionAssert.AreEquivalent(new[] { "Heat (1995)", "Old" },
            result.Issues.Where(i => i.Kind == MediaDiskIssueKind.EmptyFolder).Select(i => Path.GetFileName(i.Path)).ToArray(),
            "Heat's folder is now empty; Old is reported, not Older inside it");
        Assert.AreEqual(6, result.IssueCount);
    }

    [TestMethod]
    public async Task TestCheckOfAFolderThatIsNotThere()
    {
        using var db = new SqlServerTestDatabase();
        string missing = Path.Combine(Path.GetTempPath(), "pda_missing_" + Guid.NewGuid().ToString("N"));

        MediaDiskCheckResult result = await new MediaDiskCheckService(new MediaAnalysisService(db.ContextFactory)).CheckAsync(MediaAnalysisKind.Music, missing);

        Assert.IsFalse(result.FolderFound);
        Assert.AreEqual(MediaDiskIssueKind.Unreadable, result.Issues.Single().Kind);
    }

    private static MediaScanner Scanner(SqlServerTestDatabase db) =>
        new(new MediaCategoryService(db.ContextFactory), new ArtistService(db.ContextFactory), new AlbumService(db.ContextFactory),
            new AlbumTypeService(db.ContextFactory), new TrackService(db.ContextFactory), new MovieService(db.ContextFactory),
            new TVShowService(db.ContextFactory), new TVShowEpisodeService(db.ContextFactory), new MediaCacheService(db.ContextFactory), new SettingService(db.ContextFactory));

    /// <summary>A temp folder for media files, deleted on dispose.</summary>
    private sealed class TempFolder : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "pda_disk_check_test_" + Guid.NewGuid().ToString("N"));

        public string Combine(params string[] parts) => Path.Combine([_root, .. parts]);

        /// <summary>Creates (or replaces) a file of <paramref name="size"/> bytes, and its folders.</summary>
        public void WriteFile(int size, params string[] parts)
        {
            string path = Combine(parts);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[size]);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    /// <summary>Reports at once, on the caller's thread.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
