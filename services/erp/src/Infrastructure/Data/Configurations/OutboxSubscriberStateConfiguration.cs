using ERP.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

public class OutboxSubscriberStateConfiguration : IEntityTypeConfiguration<OutboxSubscriberState> {
    public void Configure(EntityTypeBuilder<OutboxSubscriberState> builder) {
        builder.ToTable("outbox_subscriber_states");
        builder.HasKey(e => new { e.MessageId, e.Subscriber });
        builder.Property(e => e.Subscriber).HasMaxLength(100);
        builder.Property(e => e.LastError).HasMaxLength(2000);
        builder.HasIndex(e => e.NextAttemptAt).HasDatabaseName("IX_outbox_subscriber_states_Pending").HasFilter("\"CompletedAt\" IS NULL AND NOT \"GaveUp\"");
    }
}
