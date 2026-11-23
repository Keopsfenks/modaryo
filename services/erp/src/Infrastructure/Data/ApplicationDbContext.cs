using System.Linq.Expressions;
using System.Reflection;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Platform;
using ERP.Domain.Files;
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

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) {
        return Database.BeginTransactionAsync(cancellationToken);
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