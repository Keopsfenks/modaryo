using System.Linq.Expressions;
using System.Reflection;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Platform;
using ERP.Domain.Files;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Pricing;
using ERP.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IApplicationDbContext {
    private readonly ICurrentTenant _currentTenant;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenant currentTenant)
        : base(options) {
        _currentTenant = currentTenant;
    }

    protected ApplicationDbContext(DbContextOptions options, ICurrentTenant currentTenant)
        : base(options) {
        _currentTenant = currentTenant;
    }

    /// <summary>Sorgu filtresi bu değeri her sorguda (bağlam örneği başına) okur. Firma yoksa hiçbir satır görünmez.</summary>
    public Guid? CurrentTenantId => _currentTenant.TenantId;

    public DbSet<CurrentAccount> CurrentAccounts => Set<CurrentAccount>();

    public DbSet<CurrentAccountAddress> CurrentAccountAddresses => Set<CurrentAccountAddress>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<LedgerDueLine> LedgerDueLines => Set<LedgerDueLine>();

    public DbSet<LedgerAllocation> LedgerAllocations => Set<LedgerAllocation>();

    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();

    public DbSet<ErpAuditEntry> AuditEntries => Set<ErpAuditEntry>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<ProductUnit> ProductUnits => Set<ProductUnit>();

    public DbSet<ProductSupplier> ProductSuppliers => Set<ProductSupplier>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Barcode> Barcodes => Set<Barcode>();

    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<PriceList> PriceLists => Set<PriceList>();

    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();

    public DbSet<CustomerGroup> CustomerGroups => Set<CustomerGroup>();

    public DbSet<CustomerPrice> CustomerPrices => Set<CustomerPrice>();

    public DbSet<PriceHistoryEntry> PriceHistory => Set<PriceHistoryEntry>();

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<MovementLotLine> MovementLotLines => Set<MovementLotLine>();

    public DbSet<CostLayer> CostLayers => Set<CostLayer>();

    public DbSet<AverageCostSnapshot> AverageCosts => Set<AverageCostSnapshot>();

    public DbSet<Lot> Lots => Set<Lot>();

    public DbSet<SerialNumber> SerialNumbers => Set<SerialNumber>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<MinStockLevel> MinStockLevels => Set<MinStockLevel>();

    public DbSet<StockCount> StockCounts => Set<StockCount>();

    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();

    public DbSet<RevaluationJob> RevaluationJobs => Set<RevaluationJob>();

    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();

    public DbSet<Transfer> Transfers => Set<Transfer>();

    public DbSet<DocumentCounter> DocumentCounters => Set<DocumentCounter>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<InvoiceDueLine> InvoiceDueLines => Set<InvoiceDueLine>();

    public DbSet<DocumentSeries> DocumentSeries => Set<DocumentSeries>();

    public DbSet<WithholdingCode> WithholdingCodes => Set<WithholdingCode>();

    public DbSet<ExemptionCode> ExemptionCodes => Set<ExemptionCode>();

    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    public DbSet<TenantRateOverride> TenantRateOverrides => Set<TenantRateOverride>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) {
        return Database.BeginTransactionAsync(cancellationToken);
    }

    public void ClearTracking() {
        ChangeTracker.Clear();
    }

    public async Task<bool> LockCurrentAccountAsync(Guid id, CancellationToken cancellationToken) {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Cari kilidi yalnızca açık bir işlem içinde alınabilir.");

        var tenantId = CurrentTenantId;
        var locked = await Database
                         .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM current_accounts WHERE \"Id\" = {id} AND \"TenantId\" = {tenantId} FOR UPDATE")
                         .ToListAsync(cancellationToken);
        return locked.Count > 0;
    }

    public async Task LockStockAsync(IEnumerable<(Guid WarehouseId, Guid ProductId, Guid? VariantId)> items,
                                     CancellationToken cancellationToken
    ) {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stok kilidi yalnızca açık bir işlem içinde alınabilir.");

        var tenantId = CurrentTenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var list     = items.ToList();

        // Genel sıra: önce depo düzeyi PAYLAŞIMLI kilitler (sıralı), sonra kalem kilitleri (sıralı). Sayım açılışı depo kilidini
        // özel alır; böylece yazan hiçbir işlem sayım anlık görüntüsünün altında yarım kalmaz. Anahtar metni sürümler ve
        // süreçler arasında sabittir; hash PostgreSQL'de hesaplanır.
        var warehouses = list.Select(i => i.WarehouseId).Where(w => w != Guid.Empty)
                             .Select(w => $"stockwh:{tenantId:N}:{w:N}").Distinct().Order(StringComparer.Ordinal).ToList();
        foreach (var key in warehouses)
            await Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared(hashtextextended({key}, 0))", cancellationToken);

        var keys = list.Select(i => $"stock:{tenantId:N}:{i.WarehouseId:N}:{i.ProductId:N}:{i.VariantId:N}")
                       .Distinct().Order(StringComparer.Ordinal).ToList();
        foreach (var key in keys)
            await Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public async Task LockWarehouseExclusiveAsync(Guid warehouseId, CancellationToken cancellationToken) {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stok kilidi yalnızca açık bir işlem içinde alınabilir.");

        var tenantId = CurrentTenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var key      = $"stockwh:{tenantId:N}:{warehouseId:N}";
        await Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public async Task<StockProfile> StockProfileAsync(Guid              warehouseId,
                                                      Guid              productId,
                                                      Guid?             variantId,
                                                      DateOnly          date,
                                                      CancellationToken cancellationToken
    ) {
        var tenantId = CurrentTenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var rows = await Database.SqlQuery<StockProfile>($"""
                                                           SELECT COALESCE(SUM(CASE WHEN t."Date" <= {date} THEN t."Quantity" END), 0) AS "AtDate",
                                                                  MIN(CASE WHEN t."Date" > {date} THEN t."Run" END)                    AS "MinAfter",
                                                                  COALESCE(SUM(t."Quantity"), 0)                                        AS "Total",
                                                                  MAX(t."Date")                                                         AS "LastDate"
                                                           FROM (SELECT "Date", "Quantity",
                                                                        SUM("Quantity") OVER (ORDER BY "Date", "Sequence") AS "Run"
                                                                 FROM stock_movements
                                                                 WHERE "TenantId" = {tenantId} AND "WarehouseId" = {warehouseId}
                                                                   AND "ProductId" = {productId}
                                                                   AND "VariantId" IS NOT DISTINCT FROM {variantId}::uuid) t
                                                           """).ToListAsync(cancellationToken);
        return rows.Single();
    }

    public async Task<long?> ReserveSeriesNumberAsync(Guid seriesId, CancellationToken cancellationToken) {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Seri numarası yalnızca açık bir işlem içinde ayrılabilir.");

        var tenantId = CurrentTenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var rows = await Database.SqlQuery<long>($"""
                                                  UPDATE document_series SET "NextNumber" = "NextNumber" + 1
                                                  WHERE "Id" = {seriesId} AND "TenantId" = {tenantId} AND "IsActive"
                                                  RETURNING "NextNumber" - 1 AS "Value"
                                                  """).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<int> NextDocumentNumberAsync(string key, CancellationToken cancellationToken) {
        var tenantId = CurrentTenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var rows = await Database.SqlQuery<int>($"""
                                                 INSERT INTO document_counters ("TenantId", "Key", "Value") VALUES ({tenantId}, {key}, 1)
                                                 ON CONFLICT ("TenantId", "Key") DO UPDATE SET "Value" = document_counters."Value" + 1
                                                 RETURNING "Value"
                                                 """).ToListAsync(cancellationToken);
        return rows.Single();
    }

    public async Task<int> ReserveCatalogSequenceAsync(string            field,
                                                       int               atLeast,
                                                       string            seedJson,
                                                       CancellationToken cancellationToken
    ) {
        var tenantId = CurrentTenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var path     = new[] { field };
        var rows = await Database.SqlQuery<int>($"""
                                                 INSERT INTO tenant_settings ("TenantId", "Category", "Json", "Version", "UpdatedAt", "UpdatedBy")
                                                 VALUES ({tenantId}, 'katalog', {seedJson}::jsonb, 1, now(), 'sistem')
                                                 ON CONFLICT ("TenantId", "Category") DO UPDATE
                                                 SET "Json" = jsonb_set(tenant_settings."Json", {path},
                                                         to_jsonb(GREATEST(COALESCE((tenant_settings."Json" ->> {field}::text)::int, 1), {atLeast}) + 1)),
                                                     "Version" = tenant_settings."Version" + 1,
                                                     "UpdatedAt" = now()
                                                 RETURNING ((tenant_settings."Json" ->> {field}::text)::int - 1) AS "Value"
                                                 """).ToListAsync(cancellationToken);
        return rows.Single();
    }

    protected override void OnModelCreating(ModelBuilder builder) {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyTenantFilters(builder);
    }

    /// <summary>ITenantOwned uygulayan her varlığa "TenantId == aktif firma" filtresi ekler.</summary>
    protected void ApplyTenantFilters(ModelBuilder builder) {
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType))) {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var tenantId = Expression.Convert(Expression.Property(parameter, nameof(ITenantOwned.TenantId)),
                                              typeof(Guid?));
            var current = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
            var filter  = Expression.Lambda(Expression.Equal(tenantId, current), parameter);

            builder.Entity(entityType.ClrType).HasQueryFilter(filter);
            builder.Entity(entityType.ClrType).HasIndex(nameof(ITenantOwned.TenantId));
        }
    }
}