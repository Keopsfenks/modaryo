using ERP.Application.Common.Interfaces;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Allocation;

public sealed class AutoAllocator(
    IApplicationDbContext db,
    TimeProvider          time
) {
    /// <summary>
    ///     Carinin otomatik eşleştirmelerini yeniden kurar: elle eşleştirmeler kalır (iptal edilmiş bir satıra
    ///     dokunanlar silinir), kalan tutarlar FIFO ile (vade, hareket tarihi, oluşturulma, sıra) iptal edilmemiş
    ///     satırlar üzerinde eşleştirilir. Önce carinin satırını kilitler (açık işlem gerekir); değişiklikleri
    ///     bağlama ekler, SaveChanges çağıranın işidir.
    /// </summary>
    public async Task RebuildAsync(Guid currentAccountId, CancellationToken ct) {
        // POS perakende carisi: eşleştirme artımlı yazılır (PosSalePoster); her satışta tüm geçmişi yeniden kurmak yok.
        if (db.CariLockExemptions.Contains(currentAccountId)) return;

        if (!await db.LockCurrentAccountAsync(currentAccountId, ct))
            throw new NotFoundException(currentAccountId.ToString(), "CurrentAccount");

        var lines = await OpenItemsReader.ActiveLinesAsync(
                                                           db,
                                                           db.LedgerDueLines.Where(l => l.CurrentAccountId ==
                                                                                        currentAccountId), ct);
        var active   = lines.ToDictionary(l => l.Line.Id, l => l.Line);
        var existing = await db.LedgerAllocations.Where(a => a.CurrentAccountId == currentAccountId).ToListAsync(ct);

        // Değişmeyen otomatik eşleştirmeler yerinde kalır; yalnızca fark yazılır.
        var manual   = new List<LedgerAllocation>();
        var previous = new Dictionary<(Guid Debit, Guid Credit), LedgerAllocation>();
        foreach (var allocation in existing) {
            var valid = active.ContainsKey(allocation.DebitLineId) && active.ContainsKey(allocation.CreditLineId);
            if (valid && allocation.Kind == AllocationKind.Manual) {
                manual.Add(allocation);
                continue;
            }

            if (valid && allocation.Kind == AllocationKind.Auto
                      && previous.TryAdd((allocation.DebitLineId, allocation.CreditLineId), allocation))
                continue;

            db.LedgerAllocations.Remove(allocation);
        }

        foreach (var match in DueMatching.AutoAllocations(lines, manual)) {
            if (previous.Remove((match.DebitLineId, match.CreditLineId), out var kept)) {
                if (kept.Amount != match.Amount) kept.Amount = match.Amount;

                continue;
            }

            db.LedgerAllocations.Add(new LedgerAllocation {
                                                              TenantId         = active[match.DebitLineId].TenantId,
                                                              CurrentAccountId = currentAccountId,
                                                              DebitLineId      = match.DebitLineId,
                                                              CreditLineId     = match.CreditLineId,
                                                              Amount           = match.Amount,
                                                              Kind             = AllocationKind.Auto,
                                                              CreatedAt        = time.GetUtcNow()
                                                          });
        }

        db.LedgerAllocations.RemoveRange(previous.Values);
    }
}
