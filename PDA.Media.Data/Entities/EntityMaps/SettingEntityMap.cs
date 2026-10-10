using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PDA.Media.Data.Entities.EntityMaps;

public partial class SettingEntityMap : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> entity)
    {
        entity.HasKey(e => e.Key);

        entity.Property(e => e.Key).HasMaxLength(100).IsUnicode(false);
        entity.Property(e => e.Value).IsRequired().HasMaxLength(500).IsUnicode(false);
            
        OnConfigurePartial(entity);
    }
    partial void OnConfigurePartial(EntityTypeBuilder<Setting> entity);
}