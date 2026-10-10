using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PDA.Media.Data.Entities.EntityMaps;

public partial class TVShowTypeEntityMap : IEntityTypeConfiguration<TVShowType>
{
    public void Configure(EntityTypeBuilder<TVShowType> entity)
    {
        entity.ToTable("TVShowTypes");

        entity.Property(e => e.Name).IsRequired().HasMaxLength(200).IsUnicode(false);
        entity.Property(e => e.Description).HasMaxLength(500).IsUnicode(false);

        OnConfigurePartial(entity);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<TVShowType> entity);
}