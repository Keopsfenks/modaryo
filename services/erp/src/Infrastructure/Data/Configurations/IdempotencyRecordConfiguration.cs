using ERP.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord> {
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder) {
        builder.ToTable("idempotency_records");
        builder.HasKey(e => new { e.Caller, e.Key });
        builder.Property(e => e.Caller).HasMaxLength(100);
        builder.Property(e => e.Key).HasMaxLength(200);
        builder.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.ResponseJson).IsRequired();
        builder.HasIndex(e => e.CreatedAt);
    }
}
