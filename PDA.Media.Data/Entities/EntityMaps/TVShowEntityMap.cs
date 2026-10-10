using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PDA.Media.Data.Contexts;

namespace PDA.Media.Data.Entities.EntityMaps;

public class TVShowEntityMap : IEntityTypeConfiguration<TVShow>
{
    public void Configure(EntityTypeBuilder<TVShow> entity)
    {
        entity.ToTable("TVShows");

        entity.HasKey(e => e.Id).IsClustered(true);
        entity.HasIndex(e => e.Name, "IX_TVShows_Name");
        entity.HasIndex(e => e.TVShowTypeId, "IX_TVShows_TVShowTypeId");
        entity.HasIndex(e => e.IsDeleted, "IX_TVShows_IsDeleted");

        // Hide soft-deleted rows from every query (see DataContext.SoftDeleteFilter).
        entity.HasQueryFilter(DataContext.SoftDeleteFilter, e => !e.IsDeleted);

        entity.Property(e => e.Name).HasMaxLength(200);
        entity.Property(e => e.Description).HasMaxLength(500);
        entity.Property(e => e.Folder).HasMaxLength(1000);
        entity.Property(e => e.CoverArtFilename).HasMaxLength(500);
        entity.Property(e => e.TVShowTypeId).HasColumnName("TVShowTypeId");
        entity.Property(e => e.ReleaseYear).HasDefaultValue(0, "DF_TVShows_ReleaseYear");
        entity.Property(e => e.TMDB_Id).HasMaxLength(100).IsUnicode(false).HasColumnName("TMDB_Id");
            
        entity.Property(e => e.CreatedOn).HasDefaultValueSql("(sysdatetime())", "DF_TVShows_CreatedOn");
        entity.Property(e => e.CreatedBy).HasMaxLength(100).IsUnicode(false).HasDefaultValue("API", "DF_TVShows_CreatedBy");
        entity.Property(e => e.ModifiedOn).HasColumnName("UpdatedOn").HasDefaultValueSql("(sysdatetime())", "DF_TVShows_UpdatedOn");
        entity.Property(e => e.ModifiedBy).HasColumnName("UpdatedBy").HasMaxLength(100).IsUnicode(false);

        entity.HasOne(d => d.TvShowType).WithMany(p => p.TVShows)
            .HasForeignKey(d => d.TVShowTypeId)
            // The database sets the foreign key to its default (ON DELETE SET DEFAULT), which EF can't express;
            // ClientNoAction leaves loaded rows alone and lets the database do it.
            .OnDelete(DeleteBehavior.ClientNoAction)
            .HasConstraintName("FK_TVShows_TVShowTypes");
    }
}
