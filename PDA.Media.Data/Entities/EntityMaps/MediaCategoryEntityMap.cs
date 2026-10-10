using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PDA.Media.Data.Contexts;

namespace PDA.Media.Data.Entities.EntityMaps;

public class MediaCategoryEntityMap : IEntityTypeConfiguration<MediaCategory>
{
    public void Configure(EntityTypeBuilder<MediaCategory> entity)
    {
        entity.HasKey(e => e.Id);
        // Seeded at 0: matches the database (IDENTITY(0,1)).
        entity.Property(e => e.Id).UseIdentityColumn(0, 1);
        entity.HasIndex(e => e.AlbumTypeId, "IX_MediaCategories_AlbumTypeId");
            
        // Hide soft-deleted rows from every query (see DataContext.SoftDeleteFilter).
        entity.HasQueryFilter(DataContext.SoftDeleteFilter, e => !e.IsDeleted);

        entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        entity.Property(e => e.Description).HasMaxLength(500);
        // ValueGeneratedNever: EF always sends the value. Without it, EF leaves a column with a database default out
        // of the INSERT when the value is the "not set" value (0 for an int), so an album saved with TrackCount = 0
        // would be stored as 1. The C# initializer in the entity gives the same default as the database.
        entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_MediaCategories_IsActive").ValueGeneratedNever();
        entity.Property(e => e.MediaClass).HasMaxLength(50).IsUnicode(false).HasDefaultValue("Unknown", "DF_MediaCategories_MediaClass");
        entity.Property(e => e.AlbumTypeId).HasDefaultValue(5, "DF_MediaCategories_AlbumTypeId").ValueGeneratedNever();
        entity.Property(e => e.RootFolder).HasMaxLength(1000);
    }
}
