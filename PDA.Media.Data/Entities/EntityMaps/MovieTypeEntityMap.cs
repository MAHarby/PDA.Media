using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PDA.Media.Data.Entities.EntityMaps;

public partial class MovieTypeEntityMap : IEntityTypeConfiguration<MovieType>
{
    public void Configure(EntityTypeBuilder<MovieType> entity)
    {
        entity.HasIndex(e => e.Name, "IX_MovieTypes_Name");

        entity.Property(e => e.Name).IsRequired().HasMaxLength(100).IsUnicode(false);
        entity.Property(e => e.Description).HasMaxLength(500).IsUnicode(false);

        OnConfigurePartial(entity);
    }
    partial void OnConfigurePartial(EntityTypeBuilder<MovieType> entity);
}