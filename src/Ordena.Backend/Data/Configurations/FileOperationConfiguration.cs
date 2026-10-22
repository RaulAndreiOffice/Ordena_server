using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordena.Backend.Models;

namespace Ordena.Backend.Data.Configurations;

internal sealed class FileOperationConfiguration : IEntityTypeConfiguration<FileOperation>
{
    public void Configure(EntityTypeBuilder<FileOperation> builder)
    {
        builder.ToTable("FileOperations");

        // Journal rows must survive for startup recovery, so they are never cascade-deleted.
        builder.HasOne(f => f.Document)
            .WithMany()
            .HasForeignKey(f => f.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
