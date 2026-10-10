using Microsoft.EntityFrameworkCore;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Tests;

/// <summary>
/// DataContext behaviour that needs no database: audit stamping, soft-delete filters and default values.
/// SqlCaptureContext captures the SQL a save would send instead of running it.
/// </summary>
[TestClass]
public sealed class DataContextTests
{
    // Audit fields.
    // ==================================================================================================

    [TestMethod]
    public void TestAddedEntityIsStamped()
    {
        var capture = new SqlCaptureContext();
        capture.Context.AuditUser = "Tester";
        var artist = new Artist { Name = "Björk" };
        capture.Context.Artists.Add(artist);
        DateTime before = DateTime.Now;

        capture.Save();

        Assert.IsTrue(artist.CreatedOn >= before && artist.CreatedOn <= DateTime.Now);
        Assert.AreEqual(artist.CreatedOn, artist.ModifiedOn);
        Assert.AreEqual("Tester", artist.CreatedBy);
        Assert.AreEqual("Tester", artist.ModifiedBy);
    }

    [TestMethod]
    public void TestModifiedEntityKeepsItsCreatedFields()
    {
        var capture = new SqlCaptureContext();
        var created = new DateTime(2020, 1, 2, 3, 4, 5);
        var album = new Album { Id = 7, Name = "Old", CreatedOn = created, CreatedBy = "Someone", ModifiedOn = created, ModifiedBy = "Someone" };
        capture.Context.Albums.Attach(album);
        album.Name = "New";

        string sql = capture.Save();

        StringAssert.Contains(sql, "UPDATE [Albums]");
        StringAssert.Contains(sql, "[ModifiedOn] = ");
        StringAssert.Contains(sql, "[ModifiedBy] = ");
        Assert.DoesNotContain("[CreatedOn]", sql);
        Assert.DoesNotContain("[CreatedBy]", sql);
        Assert.AreEqual(created, album.CreatedOn);
        Assert.AreEqual("API", album.ModifiedBy);
        Assert.IsTrue(album.ModifiedOn > created);
    }

    [TestMethod]
    public void TestModifiedOnUsesTheUpdatedOnColumn()
    {
        var capture = new SqlCaptureContext();
        capture.Context.Movies.Add(new Movie { Name = "Alien", MovieTypeId = 1 });

        string sql = capture.Save();

        StringAssert.Contains(sql, "[UpdatedOn]");
        StringAssert.Contains(sql, "[UpdatedBy]");
        Assert.DoesNotContain("[ModifiedOn]", sql);
    }

    // Default values.
    // ==================================================================================================

    [TestMethod]
    public void TestFalseIsSavedForAColumnThatDefaultsToTrue()
    {
        var capture = new SqlCaptureContext();
        capture.Context.MediaCategories.Add(new MediaCategory { Name = "Archive", MediaClass = "Movie", IsActive = false });

        string columns = capture.SaveInsertColumns();

        // The column is in the INSERT, so the value (false) is sent rather than the database default (true).
        StringAssert.Contains(columns, "[IsActive]");
    }

    [TestMethod]
    public void TestZeroIsSavedForAColumnWithANonZeroDefault()
    {
        var capture = new SqlCaptureContext();
        capture.Context.Albums.Add(new Album { Name = "Empty", ArtistId = 1, TrackCount = 0, AlbumTypeId = 0 });

        string columns = capture.SaveInsertColumns();

        // Without ValueGeneratedNever, EF leaves out an int that is 0 and the database stores its default (1).
        StringAssert.Contains(columns, "[TrackCount]");
        StringAssert.Contains(columns, "[AlbumTypeId]");
    }

    [TestMethod]
    public void TestDefaultsMatchTheDatabase()
    {
        var album = new Album();
        var category = new MediaCategory();

        Assert.AreEqual(1, album.AlbumTypeId);
        Assert.AreEqual(1, album.TrackCount);
        Assert.AreEqual(5, category.AlbumTypeId);
        Assert.IsTrue(category.IsActive);
    }

    // Soft delete.
    // ==================================================================================================

    [TestMethod]
    [DataRow(typeof(Album))]
    [DataRow(typeof(Artist))]
    [DataRow(typeof(MediaCategory))]
    [DataRow(typeof(Movie))]
    [DataRow(typeof(Track))]
    [DataRow(typeof(TVShow))]
    [DataRow(typeof(TVShowEpisode))]
    public void TestSoftDeletedRowsAreFiltered(Type entityType)
    {
        using var context = new SqlCaptureContext().Context;
        var type = context.Model.FindEntityType(entityType)!;

        var filter = type.FindDeclaredQueryFilter(DataContext.SoftDeleteFilter);

        Assert.IsNotNull(filter, $"{entityType.Name} has no soft-delete filter");
    }

    [TestMethod]
    public void TestQueriesExcludeDeletedRowsUnlessAsked()
    {
        using var context = new SqlCaptureContext().Context;

        string filtered = context.Albums.ToQueryString();
        string unfiltered = context.Albums.IgnoreQueryFilters([DataContext.SoftDeleteFilter]).ToQueryString();

        StringAssert.Contains(filtered, "[a].[IsDeleted] = CAST(0 AS bit)");
        Assert.DoesNotContain("WHERE", unfiltered);
    }
}
