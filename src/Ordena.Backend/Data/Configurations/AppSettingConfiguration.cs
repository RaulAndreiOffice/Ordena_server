using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordena.Backend.Models;

namespace Ordena.Backend.Data.Configurations;

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("Settings");

        builder.HasKey(s => s.Key);
    }
}
