using ERP.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

public class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting> {
    public void Configure(EntityTypeBuilder<TenantSetting> builder) {
        builder.ToTable("tenant_settings");
        builder.HasKey(e => new { e.TenantId, e.Category });
        builder.Property(e => e.Category).HasMaxLength(50);
        builder.Property(e => e.Json).HasColumnType("jsonb").IsRequired();
        // Eşzamanlı yazarlardan yalnızca biri başarılı olsun: UPDATE ... WHERE "Version" = <okunan sürüm>.
        builder.Property(e => e.Version).IsConcurrencyToken();
        builder.Property(e => e.UpdatedBy).HasMaxLength(200).IsRequired();
    }
}

public class ErpAuditEntryConfiguration : IEntityTypeConfiguration<ErpAuditEntry> {
    public void Configure(EntityTypeBuilder<ErpAuditEntry> builder) {
        builder.ToTable("erp_audit_entries");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ActorLabel).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(100).IsRequired();
        builder.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Before).HasColumnType("jsonb");
        builder.Property(e => e.After).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.TenantId, e.At }).IsDescending(false, true);
    }
}
