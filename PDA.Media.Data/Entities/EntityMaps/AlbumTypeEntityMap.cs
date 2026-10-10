using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PDA.Media.Data.Entities.EntityMaps;

public partial class AlbumTypeEntityMap : IEntityTypeConfiguration<AlbumType>
{
    public void Configure(EntityTypeBuilder<AlbumType> entity)
    {
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Name).IsRequired().HasMaxLength(200).IsUnicode(false);
        entity.Property(e => e.Description).HasMaxLength(500).IsUnicode(false);
            
        OnConfigurePartial(entity);
    }
    partial void OnConfigurePartial(EntityTypeBuilder<AlbumType> entity);
}