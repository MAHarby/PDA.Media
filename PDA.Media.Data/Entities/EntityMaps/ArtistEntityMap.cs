using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PDA.Media.Data.Contexts;

namespace PDA.Media.Data.Entities.EntityMaps;

public class ArtistEntityMap : IEntityTypeConfiguration<Artist>
{
    public void Configure(EntityTypeBuilder<Artist> entity)
    {
        entity.HasKey(e => e.Id).IsClustered(false);
        entity.HasIndex(e => e.Name, "IX_Artists_Name").IsClustered();
        entity.HasIndex(e => e.IsDeleted, "IX_Artists_IsDeleted");
        entity.HasIndex(e => e.IsFavourite, "IX_Artists_IsFavourite");

        // Hide soft-deleted rows from every query (see DataContext.SoftDeleteFilter).
        entity.HasQueryFilter(DataContext.SoftDeleteFilter, e => !e.IsDeleted);

        entity.Property(e => e.Name).IsRequired().HasMaxLength(200).IsUnicode(false);
        entity.Property(e => e.Description).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.Folder).HasMaxLength(1000).IsUnicode(false); entity.Property(e => e.MusicBrainzId).HasMaxLength(100).IsUnicode(false);
        entity.Property(e => e.Notes).HasColumnType("text");
            
        entity.Property(e => e.CreatedOn).HasDefaultValueSql("(GETDATE())", "DF_Artists_CreatedOn").HasColumnType("datetime");
        entity.Property(e => e.CreatedBy).HasMaxLength(100).IsUnicode(false).HasDefaultValue("API", "DF_Artists_CreatedBy");
        entity.Property(e => e.ModifiedOn).HasDefaultValueSql("(GETDATE())", "DF_Artists_ModifiedOn").HasColumnType("datetime");
        entity.Property(e => e.ModifiedBy).HasMaxLength(100).IsUnicode(false);
    }
}
