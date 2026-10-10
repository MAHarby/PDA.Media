using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PDA.Media.Data.Entities.EntityMaps;

public class TVShowTypeEntityMap : IEntityTypeConfiguration<TVShowType>
{
    public void Configure(EntityTypeBuilder<TVShowType> entity)
    {
        entity.ToTable("TVShowTypes");
        // Seeded at 0: the foreign keys that point here default to Id 0 (DEFAULT 0, ON DELETE SET DEFAULT).
        entity.Property(e => e.Id).UseIdentityColumn(0, 1);

        entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        entity.Property(e => e.Description).HasMaxLength(500);
    }
}
