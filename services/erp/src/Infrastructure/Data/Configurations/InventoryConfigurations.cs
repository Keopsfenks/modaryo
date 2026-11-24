using ERP.Domain.Catalog;
using ERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

internal static class InventoryIndexes {
    /// <summary>
    ///     (tenant, depo, ürün, varyant) anahtarı için benzersiz indeks. NULL varyant PostgreSQL'de birbirinden
    ///     farklı sayıldığından varyantlı ve varyantsız satırlar için iki kısmi indeks tanımlanır.
    /// </summary>
    public static void UniquePerItem<T>(this EntityTypeBuilder<T> builder, string warehouse = "WarehouseId") where T : class {
        builder.HasIndex("TenantId", warehouse, "ProductId").IsUnique().HasFilter("\"VariantId\" IS NULL");
        builder.HasIndex("TenantId", warehouse, "ProductId", "VariantId").IsUnique().HasFilter("\"VariantId\" IS NOT NULL");
    }
}

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse> {
    public void Configure(EntityTypeBuilder<Warehouse> builder) {
        builder.ToTable("warehouses");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.BranchId });
        // Şube başına tek varsayılan depo.
        builder.HasIndex(e => new { e.TenantId, e.BranchId }).IsUnique().HasFilter("\"IsDefault\"")
            .HasDatabaseName("IX_warehouses_TenantId_BranchId_default");
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement> {
    public void Configure(EntityTypeBuilder<StockMovement> builder) {
        builder.ToTable("stock_movements");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Sequence).UseIdentityAlwaysColumn();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.UnitCost).HasPrecision(18, 6);
        builder.Property(e => e.TotalCost).HasPrecision(18, 2);
        builder.Property(e => e.SourceType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.ActorLabel).HasMaxLength(200).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.WarehouseId, e.ProductId, e.VariantId, e.Date, e.Sequence });
        builder.HasIndex(e => new { e.TenantId, e.SourceType, e.SourceId });
        builder.HasIndex(e => e.Sequence).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.ProductId });

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.LotLines).WithOne().HasForeignKey(l => l.MovementId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MovementLotLineConfiguration : IEntityTypeConfiguration<MovementLotLine> {
    public void Configure(EntityTypeBuilder<MovementLotLine> builder) {
        builder.ToTable("movement_lot_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Quantity).HasPrecision(18, 6);

        builder.HasIndex(e => e.LotId);
        builder.HasIndex(e => e.SerialId);

        builder.HasOne<Lot>().WithMany().HasForeignKey(e => e.LotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SerialNumber>().WithMany().HasForeignKey(e => e.SerialId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CostLayerConfiguration : IEntityTypeConfiguration<CostLayer> {
    public void Configure(EntityTypeBuilder<CostLayer> builder) {
        builder.ToTable("cost_layers");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.OriginalQty).HasPrecision(18, 6);
        builder.Property(e => e.RemainingQty).HasPrecision(18, 6);
        builder.Property(e => e.UnitCost).HasPrecision(18, 6);

        builder.HasIndex(e => new { e.TenantId, e.WarehouseId, e.ProductId, e.VariantId, e.Date, e.Sequence });
        builder.HasIndex(e => e.InboundMovementId);

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(e => e.InboundMovementId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AverageCostSnapshotConfiguration : IEntityTypeConfiguration<AverageCostSnapshot> {
    public void Configure(EntityTypeBuilder<AverageCostSnapshot> builder) {
        builder.ToTable("average_cost_snapshots");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.AverageCost).HasPrecision(18, 6);

        builder.UniquePerItem();

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LotConfiguration : IEntityTypeConfiguration<Lot> {
    public void Configure(EntityTypeBuilder<Lot> builder) {
        builder.ToTable("lots");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.LotNo).HasMaxLength(60).IsRequired();

        // (tenant, ürün, varyant, lot no) benzersiz; NULL varyant için ayrı kısmi indeks.
        builder.HasIndex(e => new { e.TenantId, e.ProductId, e.LotNo }).IsUnique().HasFilter("\"VariantId\" IS NULL");
        builder.HasIndex(e => new { e.TenantId, e.ProductId, e.VariantId, e.LotNo }).IsUnique()
            .HasFilter("\"VariantId\" IS NOT NULL");

        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SerialNumberConfiguration : IEntityTypeConfiguration<SerialNumber> {
    public void Configure(EntityTypeBuilder<SerialNumber> builder) {
        builder.ToTable("serial_numbers");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Serial).HasMaxLength(100).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.ProductId, e.Serial }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.WarehouseId });

        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation> {
    public void Configure(EntityTypeBuilder<Reservation> builder) {
        builder.ToTable("reservations");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.SourceType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.TenantId, e.WarehouseId, e.ProductId, e.VariantId, e.Status });
        builder.HasIndex(e => new { e.TenantId, e.SourceType, e.SourceId });

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MinStockLevelConfiguration : IEntityTypeConfiguration<MinStockLevel> {
    public void Configure(EntityTypeBuilder<MinStockLevel> builder) {
        builder.ToTable("min_stock_levels");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MinQty).HasPrecision(18, 6);
        builder.Property(e => e.ReorderQty).HasPrecision(18, 6);

        builder.UniquePerItem();

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockCountConfiguration : IEntityTypeConfiguration<StockCount> {
    public void Configure(EntityTypeBuilder<StockCount> builder) {
        builder.ToTable("stock_counts");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Note).HasMaxLength(500);

        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        // Depo başına tek açık sayım.
        builder.HasIndex(e => new { e.TenantId, e.WarehouseId }).IsUnique().HasFilter("\"Status\" = 'Open'")
            .HasDatabaseName("IX_stock_counts_TenantId_WarehouseId_open");

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.StockCountId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine> {
    public void Configure(EntityTypeBuilder<StockCountLine> builder) {
        builder.ToTable("stock_count_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ExpectedQty).HasPrecision(18, 6);
        builder.Property(e => e.CountedQty).HasPrecision(18, 6);

        builder.HasIndex(e => new { e.StockCountId, e.ProductId, e.VariantId });

        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RevaluationJobConfiguration : IEntityTypeConfiguration<RevaluationJob> {
    public void Configure(EntityTypeBuilder<RevaluationJob> builder) {
        builder.ToTable("revaluation_jobs");
        builder.Property(e => e.Id).ValueGeneratedNever();

        // Aynı anahtar tek iş; en erken tarihe birleşir.
        builder.UniquePerItem();

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Cascade);
    }
}
