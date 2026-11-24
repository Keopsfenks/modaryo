using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Files;
using ERP.Domain.Inventory;
using ERP.Domain.Pricing;
using ERP.Domain.Settings;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.Application.Common.Interfaces;

public interface IApplicationDbContext {
    DbSet<CurrentAccount> CurrentAccounts { get; }

    DbSet<CurrentAccountAddress> CurrentAccountAddresses { get; }

    DbSet<LedgerEntry> LedgerEntries { get; }

    DbSet<LedgerDueLine> LedgerDueLines { get; }

    DbSet<LedgerAllocation> LedgerAllocations { get; }

    DbSet<TenantSetting> TenantSettings { get; }

    DbSet<ErpAuditEntry> AuditEntries { get; }

    DbSet<StoredFile> StoredFiles { get; }

    DbSet<Product> Products { get; }

    DbSet<ProductVariant> ProductVariants { get; }

    DbSet<ProductUnit> ProductUnits { get; }

    DbSet<ProductSupplier> ProductSuppliers { get; }

    DbSet<ProductImage> ProductImages { get; }

    DbSet<Category> Categories { get; }

    DbSet<Brand> Brands { get; }

    DbSet<Barcode> Barcodes { get; }

    DbSet<CustomFieldDefinition> CustomFieldDefinitions { get; }

    DbSet<ImportBatch> ImportBatches { get; }

    DbSet<PriceList> PriceLists { get; }

    DbSet<PriceListItem> PriceListItems { get; }

    DbSet<CustomerGroup> CustomerGroups { get; }

    DbSet<CustomerPrice> CustomerPrices { get; }

    DbSet<PriceHistoryEntry> PriceHistory { get; }

    DbSet<Campaign> Campaigns { get; }

    DbSet<Warehouse> Warehouses { get; }

    DbSet<StockMovement> StockMovements { get; }

    DbSet<MovementLotLine> MovementLotLines { get; }

    DbSet<CostLayer> CostLayers { get; }

    DbSet<AverageCostSnapshot> AverageCosts { get; }

    DbSet<Lot> Lots { get; }

    DbSet<SerialNumber> SerialNumbers { get; }

    DbSet<Reservation> Reservations { get; }

    DbSet<MinStockLevel> MinStockLevels { get; }

    DbSet<StockCount> StockCounts { get; }

    DbSet<StockCountLine> StockCountLines { get; }

    DbSet<RevaluationJob> RevaluationJobs { get; }

    /// <summary>İzlenen tüm varlıkları bırakır (geri alınan bir işlemden sonra bağlamı temizlemek için).</summary>
    void ClearTracking();

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Aktif firmanın carisini işlem sonuna kadar kilitler (SELECT ... FOR UPDATE); aynı carideki yazımlar sıraya girer.
    ///     Açık bir işlem gerekir. Cari bu firmada yoksa false döner.
    /// </summary>
    Task<bool> LockCurrentAccountAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     Katalog ayarlarındaki sıra sayacından tek SQL ifadesiyle (UPDATE ... RETURNING) sayı ayırır:
    ///     verilen sayı = max(sayaç, atLeast), sayaç = verilen + 1. Ayar satırı yoksa varsayılanlarla oluşturulur.
    ///     field: "nextCodeSequence" | "nextBarcodeSequence". Etkin firma gerekir.
    /// </summary>
    Task<int> ReserveCatalogSequenceAsync(string field, int atLeast, string seedJson, CancellationToken cancellationToken);
}