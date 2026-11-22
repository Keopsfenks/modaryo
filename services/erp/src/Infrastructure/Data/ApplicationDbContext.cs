using System.Linq.Expressions;
using System.Reflection;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Common;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Platform;
using ERP.Domain.Settings;
using Microsoft.EntityFrameworkCore;

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

    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();

    public DbSet<ErpAuditEntry> AuditEntries => Set<ErpAuditEntry>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

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