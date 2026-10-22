using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordena.Backend.Models;

namespace Ordena.Backend.Data.Configurations;

internal sealed class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("Partners");

        // Windows folder names ignore case, so "Repsol" and "repsol" must collide here too.
        builder.Property(p => p.FolderName).UseCollation("NOCASE");

        builder.HasIndex(p => p.FolderName).IsUnique();
        builder.HasIndex(p => p.NameNorm);
    }
}
