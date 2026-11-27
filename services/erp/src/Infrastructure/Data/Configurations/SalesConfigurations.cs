using System.Text.Json;
using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

internal static class SalesMapping {
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Money(EntityTypeBuilder b, params string[] names) {
        foreach (var n in names) b.Property(n).HasPrecision(18, 2);
    }

    public static void AsJson<T>(PropertyBuilder<IReadOnlyList<T>?> property) {
        property.HasColumnType("jsonb");
        property.HasConversion(v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                               v => v == null ? null : (IReadOnlyList<T>?)JsonSerializer.Deserialize<List<T>>(v, JsonOptions));
        property.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<T>?>(
                                               (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                                               v => v == null ? 0 : JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                                               v => v == null ? null : (IReadOnlyList<T>?)JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)));
    }
}

public class QuoteConfiguration : IEntityTypeConfiguration<Quote> {
    public void Configure(EntityTypeBuilder<Quote> builder) {
        builder.ToTable("quotes");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.SalesRepName).HasMaxLength(200);
        builder.Property(e => e.RejectReason).HasMaxLength(200);
        builder.Property(e => e.Version).AsXmin();
        builder.Ignore(e => e.Current);

        builder.HasMany(e => e.Revisions).WithOne().HasForeignKey(r => r.QuoteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Status });
        builder.HasIndex(e => new { e.TenantId, e.CurrentAccountId });
        builder.HasIndex(e => new { e.TenantId, e.SalesRepUserId });
    }
}

public class QuoteRevisionConfiguration : IEntityTypeConfiguration<QuoteRevision> {
    public void Configure(EntityTypeBuilder<QuoteRevision> builder) {
        builder.ToTable("quote_revisions");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.DocDiscount).HasPrecision(5, 2);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        SalesMapping.Money(builder, nameof(QuoteRevision.Gross), nameof(QuoteRevision.Discount), nameof(QuoteRevision.Net), nameof(QuoteRevision.Vat), nameof(QuoteRevision.Total));

        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.QuoteRevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.QuoteId, e.Revision }).IsUnique();
    }
}

public class QuoteLineConfiguration : IEntityTypeConfiguration<QuoteLine> {
    public void Configure(EntityTypeBuilder<QuoteLine> builder) {
        builder.ToTable("quote_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.SupplyMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Factor).HasPrecision(18, 6);
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.Width).HasPrecision(18, 4);
        builder.Property(e => e.Length).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 6);
        builder.Property(e => e.DiscountPercent).HasPrecision(5, 2);
        SalesMapping.Money(builder, nameof(QuoteLine.Net), nameof(QuoteLine.Vat), nameof(QuoteLine.Total));

        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.QuoteRevisionId, e.Sort }).IsUnique();
    }
}

public class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder> {
    public void Configure(EntityTypeBuilder<SalesOrder> builder) {
        builder.ToTable("sales_orders");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.InvoiceState).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.SalesRepName).HasMaxLength(200);
        builder.Property(e => e.DocDiscount).HasPrecision(5, 2);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.CancelReason).HasMaxLength(200);
        builder.Property(e => e.PrepaymentPercent).HasPrecision(5, 2);
        builder.Property(e => e.PrepaymentAmount).HasPrecision(18, 2);
        SalesMapping.Money(builder, nameof(SalesOrder.Gross), nameof(SalesOrder.Discount), nameof(SalesOrder.Net), nameof(SalesOrder.Vat), nameof(SalesOrder.Total));
        builder.Property(e => e.Version).AsXmin();
        builder.Ignore(e => e.IsEditable);

        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Plan).WithOne().HasForeignKey<InstallmentPlan>(p => p.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Quote>().WithMany().HasForeignKey(e => e.QuoteId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Status, e.Date });
        builder.HasIndex(e => new { e.TenantId, e.CurrentAccountId });
        builder.HasIndex(e => new { e.TenantId, e.SalesRepUserId });
        builder.HasIndex(e => e.QuoteId);
    }
}

public class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine> {
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder) {
        builder.ToTable("sales_order_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.SupplyMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Factor).HasPrecision(18, 6);
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.Width).HasPrecision(18, 4);
        builder.Property(e => e.Length).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 6);
        builder.Property(e => e.DiscountPercent).HasPrecision(5, 2);
        builder.Property(e => e.ShippedQuantity).HasPrecision(18, 6);
        builder.Property(e => e.InvoicedQuantity).HasPrecision(18, 6);
        builder.Property(e => e.ReturnedQuantity).HasPrecision(18, 6);
        SalesMapping.Money(builder, nameof(SalesOrderLine.Net), nameof(SalesOrderLine.Vat), nameof(SalesOrderLine.Total));
        builder.Ignore(e => e.OpenQuantity);

        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.SalesOrderId, e.Sort }).IsUnique();
        builder.HasIndex(e => e.ProductId);
        builder.HasIndex(e => e.PurchaseOrderLineId);
    }
}

public class InstallmentPlanConfiguration : IEntityTypeConfiguration<InstallmentPlan> {
    public void Configure(EntityTypeBuilder<InstallmentPlan> builder) {
        builder.ToTable("installment_plans");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.DownPayment).HasPrecision(18, 2);
        builder.Property(e => e.DownPaymentRemaining).HasPrecision(18, 2);
        builder.HasMany(e => e.Rows).WithOne().HasForeignKey(r => r.InstallmentPlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.SalesOrderId).IsUnique();
    }
}

public class InstallmentPlanRowConfiguration : IEntityTypeConfiguration<InstallmentPlanRow> {
    public void Configure(EntityTypeBuilder<InstallmentPlanRow> builder) {
        builder.ToTable("installment_plan_rows");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Remaining).HasPrecision(18, 2);
        builder.HasIndex(e => new { e.InstallmentPlanId, e.Sequence }).IsUnique();
    }
}

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment> {
    public void Configure(EntityTypeBuilder<Shipment> builder) {
        builder.ToTable("shipments");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CargoNote).HasMaxLength(500);
        builder.Property(e => e.ReceivedBy).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.CancelReason).HasMaxLength(200);
        builder.Property(e => e.CarrierCode).HasMaxLength(30);
        builder.Property(e => e.TrackingNo).HasMaxLength(100);
        builder.Property(e => e.IrsaliyeNo).HasMaxLength(30);
        builder.Property(e => e.Vehicle).HasMaxLength(100);
        builder.Property(e => e.Driver).HasMaxLength(100);
        builder.Property(e => e.Version).AsXmin();
        builder.OwnsOne(e => e.DeliveryAddress, o => o.ToJson());

        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(e => e.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.SalesOrderId });
        builder.HasIndex(e => new { e.TenantId, e.Status, e.Date });
        builder.HasIndex(e => e.InvoiceId);
        builder.HasIndex(e => e.PickListId);
    }
}

public class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine> {
    public void Configure(EntityTypeBuilder<ShipmentLine> builder) {
        builder.ToTable("shipment_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Factor).HasPrecision(18, 6);
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.Width).HasPrecision(18, 4);
        builder.Property(e => e.Length).HasPrecision(18, 4);
        builder.Property(e => e.InvoicedQuantity).HasPrecision(18, 6);
        SalesMapping.AsJson<InvoiceLot>(builder.Property(e => e.Lots));
        SalesMapping.AsJson<string>(builder.Property(e => e.Serials));

        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesOrderLine>().WithMany().HasForeignKey(e => e.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.ShipmentId, e.Sort }).IsUnique();
        builder.HasIndex(e => e.SalesOrderLineId);
    }
}

public class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn> {
    public void Configure(EntityTypeBuilder<SalesReturn> builder) {
        builder.ToTable("sales_returns");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.WarehouseKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Reason).HasMaxLength(500);
        builder.Property(e => e.Version).AsXmin();

        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.SalesReturnId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.InvoiceId });
    }
}

public class SalesReturnLineConfiguration : IEntityTypeConfiguration<SalesReturnLine> {
    public void Configure(EntityTypeBuilder<SalesReturnLine> builder) {
        builder.ToTable("sales_return_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Factor).HasPrecision(18, 6);
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.UnitCost).HasPrecision(18, 6);
        builder.Property(e => e.Width).HasPrecision(18, 4);
        builder.Property(e => e.Length).HasPrecision(18, 4);

        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.SalesReturnId, e.Sort }).IsUnique();
        builder.HasIndex(e => e.InvoiceLineId);
    }
}

public class RecurringTemplateConfiguration : IEntityTypeConfiguration<RecurringTemplate> {
    public void Configure(EntityTypeBuilder<RecurringTemplate> builder) {
        builder.ToTable("recurring_templates");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Interval).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.RecurringTemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Runs).WithOne().HasForeignKey(r => r.RecurringTemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
    }
}

public class RecurringTemplateLineConfiguration : IEntityTypeConfiguration<RecurringTemplateLine> {
    public void Configure(EntityTypeBuilder<RecurringTemplateLine> builder) {
        builder.ToTable("recurring_template_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 6);
        builder.Property(e => e.DiscountPercent).HasPrecision(5, 2);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.RecurringTemplateId, e.Sort }).IsUnique();
    }
}

public class RecurringRunConfiguration : IEntityTypeConfiguration<RecurringRun> {
    public void Configure(EntityTypeBuilder<RecurringRun> builder) {
        builder.ToTable("recurring_runs");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.HasIndex(e => new { e.RecurringTemplateId, e.Period }).IsUnique();
        builder.HasIndex(e => e.InvoiceId);
    }
}

public class SalesTargetConfiguration : IEntityTypeConfiguration<SalesTarget> {
    public void Configure(EntityTypeBuilder<SalesTarget> builder) {
        builder.ToTable("sales_targets");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.UserName).HasMaxLength(200);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.Year, e.Month }).IsUnique();
    }
}

public class InvoicePlanConsumptionConfiguration : IEntityTypeConfiguration<InvoicePlanConsumption> {
    public void Configure(EntityTypeBuilder<InvoicePlanConsumption> builder) {
        builder.ToTable("invoice_plan_consumptions");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.HasIndex(e => e.InvoiceId);
        builder.HasIndex(e => e.SalesOrderId);
    }
}
