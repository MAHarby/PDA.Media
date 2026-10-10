using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PDA.Media.Data.Entities.EntityMaps;

public partial class TVShowEntityMap : IEntityTypeConfiguration<TVShow>
{
    public void Configure(EntityTypeBuilder<TVShow> entity)
    {
        entity.ToTable("TVShows");

        entity.HasKey(e => e.Id).IsClustered(true);
        entity.HasIndex(e => e.Name, "IX_TVShows_Name");
        entity.HasIndex(e => e.TVShowTypeId, "IX_TVShows_TVShowTypeId");
        entity.HasIndex(e => e.IsDeleted, "IX_TVShows_IsDeleted");

        entity.Property(e => e.Name).HasMaxLength(200).IsUnicode(false);
        entity.Property(e => e.Description).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.Folder).HasMaxLength(1000).IsUnicode(false);
        entity.Property(e => e.CoverArtFilename).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.TVShowTypeId).HasColumnName("TVShowTypeId");
        entity.Property(e => e.Notes).HasColumnType("text");
        entity.Property(e => e.ReleaseYear).HasDefaultValue(0, "DF_TVShows_ReleaseYear");
        entity.Property(e => e.TMDB_Id).HasMaxLength(100).IsUnicode(false).HasColumnName("TMDB_Id");
            
        entity.Property(e => e.CreatedOn).HasDefaultValueSql("(getdate())", "DF_TVShows_CreatedOn").HasColumnType("datetime");
        entity.Property(e => e.CreatedBy).HasMaxLength(100).IsUnicode(false).HasDefaultValue("API", "DF_TVShows_CreatedBy");
        entity.Property(e => e.UpdatedOn).HasDefaultValueSql("(getdate())", "DF_TVShows_UpdatedOn").HasColumnType("datetime");
        entity.Property(e => e.UpdatedBy).HasMaxLength(100).IsUnicode(false);

        entity.HasOne(d => d.TvShowType).WithMany(p => p.TVShows)
            .HasForeignKey(d => d.TVShowTypeId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_TVShows_TVShowTypes");
        
        OnConfigurePartial(entity);
    }
    partial void OnConfigurePartial(EntityTypeBuilder<TVShow> entity);
}