using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PDA.Media.Data.Entities.EntityMaps;

public partial class MediaCategoryEntityMap : IEntityTypeConfiguration<MediaCategory>
{
    public void Configure(EntityTypeBuilder<MediaCategory> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.AlbumTypeId, "IX_MediaCategories_AlbumTypeId");
            
        entity.Property(e => e.Name).IsRequired().HasMaxLength(200).IsUnicode(false);
        entity.Property(e => e.Description).HasMaxLength(500).IsUnicode(false);
        entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_MediaCategories_IsActive");
        entity.Property(e => e.MediaClass).HasMaxLength(50).IsUnicode(false).HasDefaultValue("Unknown", "DF_MediaCategories_MediaClass");
        entity.Property(e => e.AlbumTypeId).HasDefaultValue(5, "DF_MediaCategories_AlbumTypeId");
        entity.Property(e => e.Notes).HasColumnType("text");
        entity.Property(e => e.RootFolder).HasMaxLength(1000).IsUnicode(false);

        OnConfigurePartial(entity);
    }
    partial void OnConfigurePartial(EntityTypeBuilder<MediaCategory> entity);
}