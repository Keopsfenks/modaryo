using ERP.Domain.CurrentAccounts;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<CurrentAccount> CurrentAccounts { get; }

    DbSet<CurrentAccountAddress> CurrentAccountAddresses { get; }

    DbSet<LedgerEntry> LedgerEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
