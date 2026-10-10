using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PDA.Media.Data.Entities.EntityMaps;

public partial class TVShowEpisodeEntityMap : IEntityTypeConfiguration<TVShowEpisode>
{
    public void Configure(EntityTypeBuilder<TVShowEpisode> entity)
    {
        entity.ToTable("TVShowEpisodes");

        entity.HasKey(e => e.Id).IsClustered(false);
        entity.HasIndex(e => new { e.TVShowId, e.Id }, "IX_TVShowEpisodes_Clustered").IsClustered();
        entity.HasIndex(e => e.Name, "IX_TVShowEpisodes_Name");
        entity.HasIndex(e => e.TVShowId, "IX_TVShowEpisodes_TVShowId");
        entity.HasIndex(e => e.IsDeleted, "IX_TVShowEpisodes_IsDeleted");

        entity.Property(e => e.Name).HasMaxLength(200).IsUnicode(false);
        entity.Property(e => e.Description).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.TVShowId).HasColumnName("TVShowId");
        entity.Property(e => e.Folder).HasMaxLength(1000).IsUnicode(false);
        entity.Property(e => e.OriginalFilename).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.CoverArtFilename).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.Notes).HasColumnType("text");
        entity.Property(e => e.ReleaseDate).HasColumnType("datetime");
        entity.Property(e => e.TMDB_Id).HasMaxLength(100).IsUnicode(false).HasColumnName("TMDB_Id");

        entity.Property(e => e.CreatedOn).HasDefaultValueSql("(getdate())", "DF_TVShowEpisode_CreatedOn").HasColumnType("datetime");
        entity.Property(e => e.CreatedBy).HasMaxLength(100).IsUnicode(false).HasDefaultValue("API", "DF_TVShowEpisode_CreatedBy");
        entity.Property(e => e.UpdatedOn).HasDefaultValueSql("(getdate())", "DF_TVShowEpisode_UpdatedOn").HasColumnType("datetime");
        entity.Property(e => e.UpdatedBy).HasMaxLength(100).IsUnicode(false);
            
        entity.HasOne(d => d.TVShow).WithMany(p => p.Episodes)
            .HasForeignKey(d => d.TVShowId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_TVShowEpisodes_TVShows");

        OnConfigurePartial(entity);
    }
    partial void OnConfigurePartial(EntityTypeBuilder<TVShowEpisode> entity);
}