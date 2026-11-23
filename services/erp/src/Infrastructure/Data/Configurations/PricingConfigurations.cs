using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

internal static class PricingColumns {
    /// <summary>PostgreSQL xmin sistem sütununu iyimser eşzamanlılık belirteci olarak kullanır.</summary>
    public static PropertyBuilder<uint> AsXmin(this PropertyBuilder<uint> property) {
        return property.HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
    }
}

public class PriceListConfiguration : IEntityTypeConfiguration<PriceList> {
    public void Configure(EntityTypeBuilder<PriceList> builder) {
        builder.ToTable("price_lists");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.AdjustmentPercent).HasPrecision(9, 4);
        builder.Property(e => e.Version).AsXmin();

        builder.HasIndex(e => new { e.TenantId, e.Kind, e.IsActive });
        builder.HasIndex(e => e.BaseListId);
        builder.HasIndex(e => e.SupplierId);

        builder.HasOne<PriceList>().WithMany().HasForeignKey(e => e.BaseListId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem> {
    public void Configure(EntityTypeBuilder<PriceListItem> builder) {
        builder.ToTable("price_list_items");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Tiers).AsJsonb().HasColumnName("tiers");
        builder.Property(e => e.Version).AsXmin();

        // Aynı listede kalem + birim (+ varyant) tek satırdır; NULL varyant için ayrı kısmi indeks gerekir.
        builder.HasIndex(e => new { e.PriceListId, e.ProductId, e.Unit }).IsUnique().HasFilter("\"VariantId\" IS NULL");
        builder.HasIndex(e => new { e.PriceListId, e.ProductId, e.VariantId, e.Unit }).IsUnique()
            .HasFilter("\"VariantId\" IS NOT NULL");
        builder.HasIndex(e => new { e.TenantId, e.ProductId });

        builder.HasOne<PriceList>().WithMany().HasForeignKey(e => e.PriceListId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CustomerGroupConfiguration : IEntityTypeConfiguration<CustomerGroup> {
    public void Configure(EntityTypeBuilder<CustomerGroup> builder) {
        builder.ToTable("customer_groups");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();

        builder.HasOne<PriceList>().WithMany().HasForeignKey(e => e.SalePriceListId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerPriceConfiguration : IEntityTypeConfiguration<CustomerPrice> {
    public void Configure(EntityTypeBuilder<CustomerPrice> builder) {
        builder.ToTable("customer_prices");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Tiers).AsJsonb().HasColumnName("tiers");

        builder.HasIndex(e => new { e.TenantId, e.CurrentAccountId, e.ProductId });
        builder.HasIndex(e => e.VariantId);

        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PriceHistoryEntryConfiguration : IEntityTypeConfiguration<PriceHistoryEntry> {
    public void Configure(EntityTypeBuilder<PriceHistoryEntry> builder) {
        // Geçmiş, kalem silinse de korunur: bilinçli olarak yabancı anahtar yoktur.
        builder.ToTable("price_history_entries");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ActorLabel).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Scope).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.BeforeJson).HasColumnType("jsonb");
        builder.Property(e => e.AfterJson).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.TenantId, e.ProductId, e.At }).IsDescending(false, false, true);
        builder.HasIndex(e => new { e.TenantId, e.Scope, e.ScopeId, e.At }).IsDescending(false, false, false, true);
    }
}

public class CampaignConfiguration : IEntityTypeConfiguration<Campaign> {
    public void Configure(EntityTypeBuilder<Campaign> builder) {
        builder.ToTable("campaigns");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Percent).HasPrecision(9, 4);
        builder.Property(e => e.Days).HasColumnType("integer[]");
        builder.Property(e => e.ProductIds).HasColumnType("uuid[]");
        builder.Property(e => e.VariantIds).HasColumnType("uuid[]");
        builder.Property(e => e.CategoryIds).HasColumnType("uuid[]");

        builder.HasIndex(e => new { e.TenantId, e.IsActive });
    }
}
