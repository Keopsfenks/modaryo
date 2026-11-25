using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder> {
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder) {
        builder.ToTable("purchase_orders");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Rate).HasPrecision(18, 6);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.CancelReason).HasMaxLength(200);
        builder.Property(e => e.CloseReason).HasMaxLength(200);
        foreach (var p in new[] { nameof(PurchaseOrder.Gross), nameof(PurchaseOrder.Discount), nameof(PurchaseOrder.Net), nameof(PurchaseOrder.Vat), nameof(PurchaseOrder.Total) })
            builder.Property(p).HasPrecision(18, 2);
        builder.Property(e => e.Version).AsXmin();
        builder.Ignore(e => e.IsEditable);

        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Status, e.Date });
        builder.HasIndex(e => new { e.TenantId, e.SupplierId });
    }
}

public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine> {
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder) {
        builder.ToTable("purchase_order_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Factor).HasPrecision(18, 6);
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.Width).HasPrecision(18, 6);
        builder.Property(e => e.Length).HasPrecision(18, 6);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 6);
        builder.Property(e => e.DiscountPercent).HasPrecision(5, 2);
        builder.Property(e => e.ReceivedQuantity).HasPrecision(18, 6);
        builder.Property(e => e.ClosedQuantity).HasPrecision(18, 6);
        foreach (var p in new[] { nameof(PurchaseOrderLine.Net), nameof(PurchaseOrderLine.Vat), nameof(PurchaseOrderLine.Total) })
            builder.Property(p).HasPrecision(18, 2);
        builder.Ignore(e => e.OpenQuantity);

        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.PurchaseOrderId, e.Sort }).IsUnique();
        builder.HasIndex(e => e.ProductId);
        builder.HasIndex(e => e.SalesOrderLineId);
    }
}

public class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt> {
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder) {
        builder.ToTable("goods_receipts");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Rate).HasPrecision(18, 6);
        builder.Property(e => e.SupplierDeliveryNo).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.CancelReason).HasMaxLength(200);

        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PurchaseOrder>().WithMany().HasForeignKey(e => e.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.PurchaseOrderId });
        builder.HasIndex(e => new { e.TenantId, e.Date });
    }
}

public class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine> {
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> builder) {
        builder.ToTable("goods_receipt_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.BaseQuantity).HasPrecision(18, 6);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 6);
        builder.Property(e => e.UnitCostTl).HasPrecision(18, 6);
        builder.Property(e => e.AdjustedUnitCostTl).HasPrecision(18, 6);
        builder.Property(e => e.Quarantine).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(e => e.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.GoodsReceiptId, e.Sort }).IsUnique();
        builder.HasIndex(e => e.PurchaseOrderLineId);
        builder.HasIndex(e => e.ProductId);
    }
}

public class ImportFileConfiguration : IEntityTypeConfiguration<ImportFile> {
    public void Configure(EntityTypeBuilder<ImportFile> builder) {
        builder.ToTable("import_files");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.OriginCountry).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Incoterm).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.HasMany(e => e.Links).WithOne().HasForeignKey(l => l.ImportFileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
    }
}

public class ImportFileLinkConfiguration : IEntityTypeConfiguration<ImportFileLink> {
    public void Configure(EntityTypeBuilder<ImportFileLink> builder) {
        builder.ToTable("import_file_links");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.ImportFileId, e.Kind, e.RefId }).IsUnique();
    }
}

public class LandedCostConfiguration : IEntityTypeConfiguration<LandedCost> {
    public void Configure(EntityTypeBuilder<LandedCost> builder) {
        builder.ToTable("landed_costs");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Key).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.CancelReason).HasMaxLength(200);
        builder.Property(e => e.TotalTl).HasPrecision(18, 2);

        builder.HasMany(e => e.Sources).WithOne().HasForeignKey(s => s.LandedCostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Targets).WithOne().HasForeignKey(t => t.LandedCostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ImportFile>().WithMany().HasForeignKey(e => e.ImportFileId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => e.ImportFileId);
    }
}

public class LandedCostSourceConfiguration : IEntityTypeConfiguration<LandedCostSource> {
    public void Configure(EntityTypeBuilder<LandedCostSource> builder) {
        builder.ToTable("landed_cost_sources");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.CostType).HasMaxLength(50);
        builder.Property(e => e.AmountTl).HasPrecision(18, 2);

        builder.HasOne<Invoice>().WithMany().HasForeignKey(e => e.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.InvoiceId);
    }
}

public class LandedCostTargetConfiguration : IEntityTypeConfiguration<LandedCostTarget> {
    public void Configure(EntityTypeBuilder<LandedCostTarget> builder) {
        builder.ToTable("landed_cost_targets");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.TlValue).HasPrecision(18, 2);
        builder.Property(e => e.BaseQuantity).HasPrecision(18, 6);
        builder.Property(e => e.WeightKgPerUnit).HasPrecision(18, 6);
        builder.Property(e => e.AllocatedTl).HasPrecision(18, 2);

        builder.HasOne<GoodsReceipt>().WithMany().HasForeignKey(e => e.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceiptLine>().WithMany().HasForeignKey(e => e.ReceiptLineId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.GoodsReceiptId);
        builder.HasIndex(e => new { e.LandedCostId, e.ReceiptLineId }).IsUnique();
    }
}
