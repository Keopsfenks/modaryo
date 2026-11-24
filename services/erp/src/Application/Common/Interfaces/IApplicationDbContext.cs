using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Files;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
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

    DbSet<StockDocument> StockDocuments { get; }

    DbSet<Transfer> Transfers { get; }

    DbSet<DocumentCounter> DocumentCounters { get; }

    DbSet<Invoice> Invoices { get; }

    DbSet<InvoiceLine> InvoiceLines { get; }

    DbSet<InvoiceDueLine> InvoiceDueLines { get; }

    DbSet<DocumentSeries> DocumentSeries { get; }

    DbSet<WithholdingCode> WithholdingCodes { get; }

    DbSet<ExemptionCode> ExemptionCodes { get; }

    DbSet<ExchangeRate> ExchangeRates { get; }

    /// <summary>Firma + anahtar için sıradaki numarayı (1'den) tek SQL ifadesiyle ayırır; işlem geri alınırsa numara da geri gider.</summary>
    Task<int> NextDocumentNumberAsync(string key, CancellationToken cancellationToken);

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
    ///     Verilen depo x kalem anahtarlarını işlem sonuna kadar kilitler (pg_advisory_xact_lock). Anahtarlar sıralı alınır
    ///     (kilitlenme olmaması için). Açık bir işlem gerekir; kilit, bakiye okumalarından ÖNCE alınmalıdır.
    /// </summary>
    Task LockStockAsync(IEnumerable<(Guid WarehouseId, Guid ProductId, Guid? VariantId)> items, CancellationToken cancellationToken);

    /// <summary>
    ///     Deponun yazma kilidini ÖZEL alır: bu depoya yazan (LockStockAsync) açık işlemlerin bitmesini bekler ve yenilerini
    ///     işlem sonuna kadar engeller. Sayım anlık görüntüsü için; başka stok kilidi tutulurken çağrılmamalıdır.
    /// </summary>
    Task LockWarehouseExclusiveAsync(Guid warehouseId, CancellationToken cancellationToken);

    /// <summary>Bir depo x kalemin defter özeti: tarihe kadar bakiye, sonraki en düşük kümülatif bakiye, toplam ve son hareket tarihi.</summary>
    Task<StockProfile> StockProfileAsync(Guid warehouseId, Guid productId, Guid? variantId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>
    ///     Katalog ayarlarındaki sıra sayacından tek SQL ifadesiyle (UPDATE ... RETURNING) sayı ayırır:
    ///     verilen sayı = max(sayaç, atLeast), sayaç = verilen + 1. Ayar satırı yoksa varsayılanlarla oluşturulur.
    ///     field: "nextCodeSequence" | "nextBarcodeSequence". Etkin firma gerekir.
    /// </summary>
    Task<int> ReserveCatalogSequenceAsync(string field, int atLeast, string seedJson, CancellationToken cancellationToken);
}

/// <summary>
///     AtDate: tarih (dahil) sonuna kadar fiziksel bakiye. MinAfter: tarihten sonraki hareketlerin kümülatif bakiyelerinin
///     en düşüğü (sonraki hareket yoksa null). Total: tüm hareketlerin toplamı. LastDate: son hareket tarihi.
/// </summary>
public sealed class StockProfile {
    public decimal   AtDate   { get; set; }
    public decimal?  MinAfter { get; set; }
    public decimal   Total    { get; set; }
    public DateOnly? LastDate { get; set; }
}
