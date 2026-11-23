using ERP.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage> {
    public void Configure(EntityTypeBuilder<OutboxMessage> builder) {
        builder.ToTable("outbox_messages");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Actor).HasMaxLength(200).IsRequired();
        builder.Property(e => e.PayloadJson).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(2000);
        builder.HasIndex(e => e.Id).HasDatabaseName("IX_outbox_messages_Pending").HasFilter("\"PublishedAt\" IS NULL");
    }
}
