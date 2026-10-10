using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PDA.Media.Data.Contexts;

namespace PDA.Media.Data.Entities.EntityMaps;

public class MovieEntityMap : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> entity)
    {
        entity.HasKey(e => e.Id).IsClustered(false);
        entity.HasIndex(e => e.Name, "IX_Movies_Name_Clustered").IsClustered();
        entity.HasIndex(e => e.MovieTypeId, "IX_Movies_MovieTypeId");
        entity.HasIndex(e => e.IsDeleted, "IX_Movies_IsDeleted");

        // Hide soft-deleted rows from every query (see DataContext.SoftDeleteFilter).
        entity.HasQueryFilter(DataContext.SoftDeleteFilter, e => !e.IsDeleted);

        entity.Property(e => e.Name).HasMaxLength(200).IsUnicode(false);
        entity.Property(e => e.Description).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.Folder).HasMaxLength(1000).IsUnicode(false);
        entity.Property(e => e.OriginalFilename).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.CoverArtFilename).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.TMDB_Id).HasMaxLength(100).IsUnicode(false).HasColumnName("TMDB_Id");
        entity.Property(e => e.Notes).HasColumnType("text");
        entity.Property(e => e.CreatedOn).HasDefaultValueSql("(GETDATE())", "DF_Movies_CreatedOn").HasColumnType("datetime");
        entity.Property(e => e.CreatedBy).HasMaxLength(100).IsUnicode(false).HasDefaultValue("API", "DF_Movies_CreatedBy");
        entity.Property(e => e.ModifiedOn).HasColumnName("UpdatedOn").HasDefaultValueSql("(GETDATE())", "DF_Movies_UpdatedOn").HasColumnType("datetime");
        entity.Property(e => e.ModifiedBy).HasColumnName("UpdatedBy").HasMaxLength(100).IsUnicode(false);

        entity.HasOne(d => d.MovieType).WithMany(p => p.Movies)
            .HasForeignKey(d => d.MovieTypeId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_Movies_MovieTypes");
    }
}
