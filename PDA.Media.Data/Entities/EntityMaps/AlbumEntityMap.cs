using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PDA.Media.Data.Contexts;

namespace PDA.Media.Data.Entities.EntityMaps;

public class AlbumEntityMap : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> entity)
    {
        entity.HasKey(e => e.Id).IsClustered(false);
        entity.HasIndex(e => e.Id, "IX_Albums_ArtistId");
        entity.HasIndex(e => e.Name, "IX_Albums_Name");
        entity.HasIndex(e => e.AlbumTypeId, "IX_Albums_AlbumTypeId");
        entity.HasIndex(e => new { e.ArtistId, e.Id }, "IX_Albums_Clustered");
        entity.HasIndex(e => e.IsDeleted, "IX_Albums_IsDeleted");
        entity.HasIndex(e => e.IsFavourite, "IX_Albums_IsFavourite");

        // Hide soft-deleted rows from every query (see DataContext.SoftDeleteFilter).
        entity.HasQueryFilter(DataContext.SoftDeleteFilter, e => !e.IsDeleted);

        entity.Property(e => e.Name).HasMaxLength(200).IsUnicode(false);
        entity.Property(e => e.Description).HasMaxLength(500).IsUnicode(false);
        // ValueGeneratedNever: EF always sends the value. Without it, EF leaves a column with a database default out
        // of the INSERT when the value is the "not set" value (0 for an int), so an album saved with TrackCount = 0
        // would be stored as 1. The C# initializer in the entity gives the same default as the database.
        entity.Property(e => e.AlbumTypeId).HasDefaultValue(1, "DF_Albums_AlbumType").ValueGeneratedNever();
        entity.Property(e => e.CoverArtFilename).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.CreatedOn).HasDefaultValueSql("(GETDATE())", "DF_Albums_CreatedOn").HasColumnType("datetime");
        entity.Property(e => e.CreatedBy).HasMaxLength(100).IsUnicode(false).HasDefaultValue("API", "DF_Albums_CreatedBy");
        entity.Property(e => e.ModifiedOn).HasDefaultValueSql("(GETDATE())", "DF_Albums_ModifiedOn").HasColumnType("datetime");
        // NOT NULL in the database (string? in C# only because IAuditable is shared; DataContext always sets it).
        entity.Property(e => e.ModifiedBy).HasMaxLength(100).IsUnicode(false).IsRequired();
        entity.Property(e => e.Folder).HasMaxLength(1000).IsUnicode(false);
        entity.Property(e => e.MusicBrainzId).HasMaxLength(100).IsUnicode(false);
        entity.Property(e => e.Notes).HasColumnType("text");
        entity.Property(e => e.OriginalFilename).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.TrackCount).HasDefaultValue(1, "DF_Albums_TrackCount").ValueGeneratedNever();

        entity.HasOne(d => d.AlbumType).WithMany(p => p.Albums)
            .HasForeignKey(d => d.AlbumTypeId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_Albums_AlbumTypes");

        entity.HasOne(d => d.Artist).WithMany(p => p.Albums)
            .HasForeignKey(d => d.ArtistId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_Albums_Artists");
    }
}
