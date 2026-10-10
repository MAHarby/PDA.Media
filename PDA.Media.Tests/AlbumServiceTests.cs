using Microsoft.EntityFrameworkCore;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;

namespace PDA.Media.Tests;

/// <summary>
/// AlbumService against a real SQL Server database (see SqlServerTestDatabase; Inconclusive without one).
/// Tests with an "async" DataRow run once through the synchronous methods and once through the async ones.
/// </summary>
[TestClass]
public sealed class AlbumServiceTests
{
    // Search patterns (no database needed).
    // ==================================================================================================

    [TestMethod]
    [DataRow("abba", "%abba%", DisplayName = "No wildcard: contains")]
    [DataRow("  abba  ", "%abba%", DisplayName = "Trimmed")]
    [DataRow("Abba*", "Abba%", DisplayName = "Starts with")]
    [DataRow("*Gold", "%Gold", DisplayName = "Ends with")]
    [DataRow("Abba*Gold", "Abba%Gold", DisplayName = "Wildcard in the middle")]
    [DataRow("*Gold*", "%Gold%", DisplayName = "Wildcards at both ends")]
    [DataRow("100%", "%100[%]%", DisplayName = "LIKE wildcards are literal")]
    [DataRow("a_b [live]", "%a[_]b [[]live]%", DisplayName = "Underscore and bracket are literal")]
    public void TestSearchPattern(string term, string expected)
    {
        Assert.AreEqual(expected, AlbumService.ToLikePattern(term));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("*")]
    [DataRow("**")]
    public void TestEmptySearchMatchesEverything(string term)
    {
        Assert.IsNull(AlbumService.ToLikePattern(term));
    }

    // Adding.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestAddRecord(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);
        int artistId = AddArtist(db, "Björk");
        DateTime before = DateTime.Now.AddSeconds(-1);

        var album = new Album { ArtistId = artistId, Name = "Homogenic", TrackCount = 0 };
        Album added = async ? await service.AddRecordAsync(album) : service.AddRecord(album);

        Assert.IsGreaterThan(0, added.Id);
        Album saved = service.GetRecordById(added.Id)!;
        Assert.AreEqual("Homogenic", saved.Name);
        Assert.AreEqual(0, saved.TrackCount);
        Assert.AreEqual("API", saved.CreatedBy);
        Assert.IsGreaterThan(before, saved.CreatedOn);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestAddRecordReturnsTheExistingAlbum(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);
        int artistId = AddArtist(db, "Björk");
        Album first = service.AddRecord(new Album { ArtistId = artistId, Name = "Homogenic" });

        // Names match ignoring case (the database collation is case-insensitive).
        var again = new Album { ArtistId = artistId, Name = "HOMOGENIC" };
        Album second = async ? await service.AddRecordAsync(again) : service.AddRecord(again);

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(1, db.Scalar<int>("SELECT COUNT(*) FROM Albums"));
    }

    [TestMethod]
    public void TestAddRecordAfterSoftDeleteAddsANewAlbum()
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);
        int artistId = AddArtist(db, "Björk");
        Album first = service.AddRecord(new Album { ArtistId = artistId, Name = "Homogenic" });
        service.DeleteRecord(first.Id);

        Album second = service.AddRecord(new Album { ArtistId = artistId, Name = "Homogenic" });

        Assert.AreNotEqual(first.Id, second.Id);
    }

    [TestMethod]
    public async Task TestConcurrentAddsOfTheSameAlbumGiveOneRow()
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);
        int artistId = AddArtist(db, "Björk");

        // One service used from many tasks at once: each call has its own DataContext.
        Album[] added = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => service.AddRecordAsync(new Album { ArtistId = artistId, Name = "Homogenic" }))));

        Assert.AreEqual(1, added.Select(a => a.Id).Distinct().Count());
        Assert.AreEqual(1, db.Scalar<int>("SELECT COUNT(*) FROM Albums"));
    }

    [TestMethod]
    public void TestUnicodeNamesRoundTrip()
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);
        int artistId = AddArtist(db, "Мумий Тролль");

        Album added = service.AddRecord(new Album { ArtistId = artistId, Name = "Морская", Folder = @"\\nas\Music\Мумий Тролль\Морская" });

        Album saved = service.GetRecordById(added.Id)!;
        Assert.AreEqual("Морская", saved.Name);
        Assert.AreEqual(@"\\nas\Music\Мумий Тролль\Морская", saved.Folder);
    }

    // Reading and searching.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestReadsSkipDeletedAlbums(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);
        int artistId = AddArtist(db, "Abba");
        Album gold = service.AddRecord(new Album { ArtistId = artistId, Name = "Gold" });
        Album arrival = service.AddRecord(new Album { ArtistId = artistId, Name = "Arrival" });
        service.DeleteRecord(gold.Id);

        List<Album> all = async ? await service.GetAllRecordsAsync() : service.GetAllRecords();
        Album? deleted = async ? await service.GetRecordByIdAsync(gold.Id) : service.GetRecordById(gold.Id);
        List<Album> byArtist = async ? await service.GetAllRecordsByArtistIdAsync(artistId) : service.GetAllRecordsByArtistId(artistId);

        CollectionAssert.AreEqual(new[] { "Arrival" }, all.Select(a => a.Name).ToArray());
        Assert.IsNull(deleted);
        CollectionAssert.AreEqual(new[] { arrival.Id }, byArtist.Select(a => a.Id).ToArray());
    }

    [TestMethod]
    [DataRow("gold", "Gold,Gold Greatest Hits,More Gold", false, DisplayName = "Contains, ignoring case")]
    [DataRow("Gold*", "Gold,Gold Greatest Hits", false, DisplayName = "Starts with")]
    [DataRow("*Gold", "Gold,More Gold", false, DisplayName = "Ends with")]
    [DataRow("Gold*Hits", "Gold Greatest Hits", false, DisplayName = "Wildcard in the middle")]
    [DataRow("100%", "100% Hits", false, DisplayName = "Percent sign is literal")]
    [DataRow("*", "100% Hits,Arrival,Gold,Gold Greatest Hits,More Gold", false, DisplayName = "Everything, sorted by name")]
    [DataRow("gold", "Gold,Gold Greatest Hits,More Gold", true, DisplayName = "Async")]
    public async Task TestSearch(string term, string expected, bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);
        int artistId = AddArtist(db, "Abba");
        foreach (string name in new[] { "Gold", "More Gold", "Gold Greatest Hits", "Arrival", "100% Hits" })
            service.AddRecord(new Album { ArtistId = artistId, Name = name });

        List<Album> found = async ? await service.GetAllRecordsAsync(term) : service.GetAllRecords(term);

        Assert.AreEqual(expected, string.Join(",", found.Select(a => a.Name)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestSearchCanIncludeArtists(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);
        int artistId = AddArtist(db, "Abba");
        service.AddRecord(new Album { ArtistId = artistId, Name = "Gold" });

        List<Album> without = async ? await service.GetAllRecordsAsync("Gold", includeArtists: false) : service.GetAllRecords("Gold", includeArtists: false);
        List<Album> with = async ? await service.GetAllRecordsAsync("Gold", includeArtists: true) : service.GetAllRecords("Gold", includeArtists: true);

        Assert.IsNull(without.Single().Artist);
        Assert.AreEqual("Abba", with.Single().Artist.Name);
    }

    // Updating and deleting.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestUpdateRecordCopiesEveryColumn(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);
        int artistId = AddArtist(db, "Abba");
        Album added = service.AddRecord(new Album { ArtistId = artistId, Name = "Gold" });
        Album original = service.GetRecordById(added.Id)!;

        // A new object (Id 0, no audit values), as an edit form might build it.
        var changes = new Album
        {
            ArtistId = artistId, Name = "Gold: Greatest Hits", Description = "Compilation", TrackCount = 19, Length = 4520,
            Folder = @"\\nas\Music\Abba\Gold", OriginalFilename = "gold.flac", CoverArtFilename = "cover.jpg",
            Notes = "Remastered", IsFavourite = true, MusicBrainzId = "mbid",
        };
        Album? updated = async ? await service.UpdateRecordAsync(added.Id, changes) : service.UpdateRecord(added.Id, changes);

        Album saved = service.GetRecordById(added.Id)!;
        Assert.IsNotNull(updated);
        Assert.AreEqual(added.Id, updated.Id);
        Assert.AreEqual(original.CreatedOn, updated.CreatedOn);
        Assert.AreEqual("Gold: Greatest Hits", saved.Name);
        Assert.AreEqual("Compilation", saved.Description);
        Assert.AreEqual(19, saved.TrackCount);
        Assert.AreEqual(4520, saved.Length);
        Assert.AreEqual(@"\\nas\Music\Abba\Gold", saved.Folder);
        Assert.AreEqual("gold.flac", saved.OriginalFilename);
        Assert.AreEqual("cover.jpg", saved.CoverArtFilename);
        Assert.AreEqual("Remastered", saved.Notes);
        Assert.IsTrue(saved.IsFavourite);
        Assert.AreEqual("mbid", saved.MusicBrainzId);
        Assert.AreEqual(original.CreatedOn, saved.CreatedOn);
        Assert.AreEqual(original.CreatedBy, saved.CreatedBy);
        Assert.IsGreaterThanOrEqualTo(original.ModifiedOn, saved.ModifiedOn);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestUpdateAndDeleteOfAMissingAlbum(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);

        Album? updated = async ? await service.UpdateRecordAsync(999, new Album { Name = "x" }) : service.UpdateRecord(999, new Album { Name = "x" });
        bool deleted = async ? await service.DeleteRecordAsync(999) : service.DeleteRecord(999);

        Assert.IsNull(updated);
        Assert.IsFalse(deleted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestDeleteRecordIsASoftDelete(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);
        int artistId = AddArtist(db, "Abba");
        Album added = service.AddRecord(new Album { ArtistId = artistId, Name = "Gold" });

        bool deleted = async ? await service.DeleteRecordAsync(added) : service.DeleteRecord(added);

        Assert.IsTrue(deleted);
        Assert.IsNull(service.GetRecordById(added.Id));
        // The row is still there, flagged as deleted.
        Assert.IsTrue(db.Scalar<bool>($"SELECT IsDeleted FROM Albums WHERE Id = {added.Id}"));
        using var context = db.ContextFactory.CreateDbContext();
        Assert.IsNotNull(context.Albums.IgnoreQueryFilters([DataContext.SoftDeleteFilter]).SingleOrDefault(a => a.Id == added.Id));
    }

    // Truncating.
    // ==================================================================================================

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestTruncateTable(bool async)
    {
        using var db = new SqlServerTestDatabase();
        var service = new AlbumService(db.ContextFactory);
        int artistId = AddArtist(db, "Abba");
        Album gold = service.AddRecord(new Album { ArtistId = artistId, Name = "Gold" });
        Album arrival = service.AddRecord(new Album { ArtistId = artistId, Name = "Arrival" });
        service.DeleteRecord(arrival.Id);
        db.Execute($"INSERT INTO Tracks (AlbumId, ArtistId, Name) VALUES ({gold.Id}, {artistId}, 'Dancing Queen')");

        bool truncated = async ? await service.TruncateTableAsync() : service.TruncateTable();

        Assert.IsTrue(truncated);
        Assert.AreEqual(0, db.Scalar<int>("SELECT COUNT(*) FROM Albums"), "soft-deleted albums are removed too");
        Assert.AreEqual(0, db.Scalar<int>("SELECT COUNT(*) FROM Tracks"), "tracks are removed by the cascade");
        Assert.AreEqual(1, service.AddRecord(new Album { ArtistId = artistId, Name = "Voyage" }).Id, "ids restart at 1");
    }

    private static int AddArtist(SqlServerTestDatabase db, string name)
    {
        using var context = db.ContextFactory.CreateDbContext();
        var artist = new Artist { Name = name };
        context.Artists.Add(artist);
        context.SaveChanges();
        return artist.Id;
    }
}
