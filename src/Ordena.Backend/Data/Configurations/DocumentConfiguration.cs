using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordena.Backend.Models;

namespace Ordena.Backend.Data.Configurations;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents");

        builder.Property(d => d.Sha256).HasMaxLength(64);

        // Windows paths ignore case, so two paths that differ only by case are the same file.
        builder.Property(d => d.RelativePath).UseCollation("NOCASE");

        builder.HasIndex(d => d.Sha256).IsUnique();
        builder.HasIndex(d => d.RelativePath).IsUnique();
        builder.HasIndex(d => new { d.PartnerId, d.DocNumberNorm });
        builder.HasIndex(d => d.IssueDate);
        builder.HasIndex(d => d.DocNumberNorm);

        // A partner with documents can't be deleted; a merge moves its documents first.
        builder.HasOne(d => d.Partner)
            .WithMany(p => p.Documents)
            .HasForeignKey(d => d.PartnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
