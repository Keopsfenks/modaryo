using ERP.Domain.CurrentAccounts;
using ERP.Domain.Files;
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Aktif firmanın carisini işlem sonuna kadar kilitler (SELECT ... FOR UPDATE); aynı carideki yazımlar sıraya girer.
    ///     Açık bir işlem gerekir. Cari bu firmada yoksa false döner.
    /// </summary>
    Task<bool> LockCurrentAccountAsync(Guid id, CancellationToken cancellationToken);
}