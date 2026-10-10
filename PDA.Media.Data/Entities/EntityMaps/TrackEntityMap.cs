using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PDA.Media.Data.Entities.EntityMaps;

public partial class TrackEntityMap : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> entity)
    {
        entity.HasKey(e => e.Id).IsClustered(false);
        entity.HasIndex(e => new { e.AlbumId, e.Id }, "IX_Tracks_Clustered").IsClustered();
        entity.HasIndex(e => e.AlbumId, "IX_Tracks_AlbumId");
        entity.HasIndex(e => e.ArtistId, "IX_Tracks_ArtistId");
        entity.HasIndex(e => e.Name, "IX_Tracks_Name");
        entity.HasIndex(e => e.IsDeleted, "IX_Tracks_IsDeleted");
        entity.HasIndex(e => e.IsFavourite, "IX_Tracks_IsFavourite");

        entity.Property(e => e.Name).HasMaxLength(200).IsUnicode(false);
        entity.Property(e => e.Description).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.Folder).HasMaxLength(1000).IsUnicode(false);
        entity.Property(e => e.OriginalFilename).HasMaxLength(1000).IsUnicode(false);
        entity.Property(e => e.CoverArtFilename).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.MusicBrainzId).HasMaxLength(100).IsUnicode(false);
        entity.Property(e => e.Notes).HasColumnType("text");

        entity.Property(e => e.CreatedOn).HasDefaultValueSql("(getdate())", "DF_Tracks_CreatedOn").HasColumnType("datetime");
        entity.Property(e => e.CreatedBy).HasMaxLength(100).IsUnicode(false).HasDefaultValue("API", "DF_Tracks_CreatedBy");
        entity.Property(e => e.ModifiedOn).HasDefaultValueSql("(getdate())", "DF_Tracks_ModifiedOn").HasColumnType("datetime");
        entity.Property(e => e.ModifiedBy).HasMaxLength(100).IsUnicode(false);

        entity.HasOne(d => d.Album).WithMany(p => p.Tracks)
            .HasForeignKey(d => d.AlbumId)
            .HasConstraintName("FK_Tracks_Albums");
            
        OnConfigurePartial(entity);
    }
    partial void OnConfigurePartial(EntityTypeBuilder<Track> entity);
}