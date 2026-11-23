using ERP.Application.CurrentAccounts.Allocation;
using ERP.Domain.CurrentAccounts;
using ERP.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Data;

/// <summary>
///     Saklı açık kalemlere geçiş: vade satırı olmayan her harekete satırını yazar, ardından satırı olan her
///     carinin otomatik eşleştirmelerini yeniden kurar. "__modaryo_backfill" tablosundaki işaret satırıyla
///     bir kez çalışır; yarıda kalırsa yeniden çalıştırmak güvenlidir (eksik satırlar tamamlanır, eşleştirme
///     yeniden kurulumu aynı sonucu verir).
/// </summary>
public sealed class StoredAllocationsBackfill {
    public const string Name = "stored-allocations-v1";

    private const int BatchSize = 1000;

    private readonly RelayDatabase                      _database;
    private readonly ILogger<StoredAllocationsBackfill> _logger;
    private readonly TimeProvider                       _time;

    internal StoredAllocationsBackfill(RelayDatabase                      database,
                                       TimeProvider                       time,
                                       ILogger<StoredAllocationsBackfill> logger
    ) {
        _database = database;
        _time     = time;
        _logger   = logger;
    }

    /// <summary>Çalıştıysa true, işaret satırı zaten varsa false döner.</summary>
    public async Task<bool> RunAsync(CancellationToken ct) {
        await using var db = _database.Create();
        if (await IsAppliedAsync(db, ct)) return false;

        var created  = await CreateMissingLinesAsync(db, ct);
        var accounts = await db.LedgerDueLines.IgnoreQueryFilters().AsNoTracking()
                           .Select(l => new { l.TenantId, l.CurrentAccountId }).Distinct().ToListAsync(ct);

        foreach (var account in accounts) {
            await using var tenantDb    = _database.CreateForTenant(account.TenantId);
            await using var transaction = await tenantDb.Database.BeginTransactionAsync(ct);
            await new AutoAllocator(tenantDb, _time).RebuildAsync(account.CurrentAccountId, ct);
            await tenantDb.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await db.Database.ExecuteSqlAsync($"""
                                           INSERT INTO "__modaryo_backfill" (name, applied_at) VALUES ({Name}, {_time.GetUtcNow()})
                                           ON CONFLICT (name) DO NOTHING
                                           """, ct);
        _logger.LogInformation("{Name}: {Lines} vade satırı yazıldı, {Accounts} carinin eşleştirmesi kuruldu.", Name,
                               created, accounts.Count);
        return true;
    }

    private static async Task<bool> IsAppliedAsync(ApplicationDbContext db, CancellationToken ct) {
        var rows = await db.Database
                       .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"__modaryo_backfill\" WHERE name = {Name}")
                       .ToListAsync(ct);
        return rows.Count > 0;
    }

    private static async Task<int> CreateMissingLinesAsync(ApplicationDbContext db, CancellationToken ct) {
        var created = 0;
        while (true) {
            var entries = await db.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
                              .Where(e => !db.LedgerDueLines.IgnoreQueryFilters().Any(l => l.EntryId == e.Id))
                              .Where(e => e.Debit > 0 || e.Credit > 0)
                              .OrderBy(e => e.Id).Take(BatchSize).ToListAsync(ct);
            if (entries.Count == 0) return created;

            // Bağlamda firma denetleyicisi yok: TenantId hareketten kopyalanır (LedgerDueLine.ForEntry).
            db.LedgerDueLines.AddRange(entries.Select(LedgerDueLine.ForEntry));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            created += entries.Count;
        }
    }
}
