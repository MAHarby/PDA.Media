using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PDA.Media.Data.Contexts;

namespace PDA.Media.Data.Entities.EntityMaps;

public class ArtistEntityMap : IEntityTypeConfiguration<Artist>
{
    public void Configure(EntityTypeBuilder<Artist> entity)
    {
        entity.HasKey(e => e.Id).IsClustered(false);
        // Seeded at 0: the foreign keys that point here default to Id 0 (DEFAULT 0, ON DELETE SET DEFAULT).
        entity.Property(e => e.Id).UseIdentityColumn(0, 1);
        entity.HasIndex(e => e.Name, "IX_Artists_Name").IsClustered();
        entity.HasIndex(e => e.IsDeleted, "IX_Artists_IsDeleted");
        entity.HasIndex(e => e.IsFavourite, "IX_Artists_IsFavourite");

        // Hide soft-deleted rows from every query (see DataContext.SoftDeleteFilter).
        entity.HasQueryFilter(DataContext.SoftDeleteFilter, e => !e.IsDeleted);

        entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        entity.Property(e => e.Description).HasMaxLength(500);
        entity.Property(e => e.Folder).HasMaxLength(1000);
        entity.Property(e => e.MusicBrainzId).HasMaxLength(100).IsUnicode(false);
            
        entity.Property(e => e.CreatedOn).HasDefaultValueSql("(sysdatetime())", "DF_Artists_CreatedOn");
        entity.Property(e => e.CreatedBy).HasMaxLength(100).IsUnicode(false).HasDefaultValue("API", "DF_Artists_CreatedBy");
        entity.Property(e => e.ModifiedOn).HasDefaultValueSql("(sysdatetime())", "DF_Artists_ModifiedOn");
        entity.Property(e => e.ModifiedBy).HasMaxLength(100).IsUnicode(false);
    }
}
