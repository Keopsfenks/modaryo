using ERP.Application.Common.Interfaces;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Allocation;

/// <summary>Açık kalemleri saklı vade satırları ve eşleştirmelerden okur (okuma anında FIFO çalıştırmaz).</summary>
public sealed class OpenItemsReader(
    IApplicationDbContext db
) {
    public async Task<IReadOnlyList<OpenItem>> ForAccountAsync(Guid accountId, DateOnly today, CancellationToken ct) {
        var lines = await ActiveLinesAsync(db, db.LedgerDueLines.Where(l => l.CurrentAccountId == accountId), ct);
        var allocations = await db.LedgerAllocations.AsNoTracking().Where(a => a.CurrentAccountId == accountId)
                              .ToListAsync(ct);
        return DueMatching.OpenItems(lines, allocations, today);
    }

    /// <summary>Birden çok cari için tek sorguda okur; her istenen cari için (boş olabilir) bir liste döner.</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<OpenItem>>> ForAccountsAsync(
        IReadOnlyCollection<Guid> accountIds,
        DateOnly                  today,
        CancellationToken         ct
    ) {
        var ids = accountIds.Distinct().ToArray();
        var lines = (await ActiveLinesAsync(db, db.LedgerDueLines.Where(l => ids.Contains(l.CurrentAccountId)), ct))
            .ToLookup(l => l.Line.CurrentAccountId);
        var allocations = (await db.LedgerAllocations.AsNoTracking().Where(a => ids.Contains(a.CurrentAccountId))
                               .ToListAsync(ct))
            .ToLookup(a => a.CurrentAccountId);

        return ids.ToDictionary(id => id, id => DueMatching.OpenItems(lines[id], allocations[id], today));
    }

    /// <summary>İptal edilmemiş hareketlerin vade satırları, sıralama için hareket tarihi ve oluşturulma zamanıyla.</summary>
    internal static async Task<List<DueLineRef>> ActiveLinesAsync(IApplicationDbContext      db,
                                                                 IQueryable<LedgerDueLine> lines,
                                                                 CancellationToken         ct
    ) {
        var rows = await (from l in lines.AsNoTracking()
                          join e in db.LedgerEntries on l.EntryId equals e.Id
                          where e.CancelledAt == null
                          select new { Line = l, e.Date, e.Created })
                       .ToListAsync(ct);
        return rows.Select(r => new DueLineRef(r.Line, r.Date, r.Created, false)).ToList();
    }
}
