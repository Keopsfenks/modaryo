using ERP.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

public class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile> {
    public void Configure(EntityTypeBuilder<StoredFile> builder) {
        builder.ToTable("stored_files");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasMaxLength(20).IsRequired();
        builder.Property(e => e.FileName).HasMaxLength(255).IsRequired();
        builder.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ObjectKey).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Sha256).HasMaxLength(64).IsRequired();
    }
}
