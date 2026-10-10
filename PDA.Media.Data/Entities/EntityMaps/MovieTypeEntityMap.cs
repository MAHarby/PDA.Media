using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PDA.Media.Data.Entities.EntityMaps;

public class MovieTypeEntityMap : IEntityTypeConfiguration<MovieType>
{
    public void Configure(EntityTypeBuilder<MovieType> entity)
    {
        // Seeded at 0: the foreign keys that point here default to Id 0 (DEFAULT 0, ON DELETE SET DEFAULT).
        entity.Property(e => e.Id).UseIdentityColumn(0, 1);
        entity.HasIndex(e => e.Name, "IX_MovieTypes_Name");

        entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        entity.Property(e => e.Description).HasMaxLength(500);
    }
}
