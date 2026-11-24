using ERP.Application.Common.Interfaces;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.ExchangeDifferences.Commands;

/// <summary>Kur farkı işlemlerinin ortak adımları: cari tespiti, kilit sonrası kontrol ve işlenmiş kaydı.</summary>
internal static class Settlement {
    public static IReadOnlyList<Guid> Distinct(IReadOnlyList<Guid> ids) {
        return ids.Distinct().ToList();
    }

    /// <summary>Tahsislerin ait olduğu tek cari; bulunamayan ya da farklı carilere ait tahsis 400.</summary>
    public static async Task<Guid> CariOfAsync(IApplicationDbContext db, IReadOnlyList<Guid> ids, CancellationToken ct) {
        var caris = await db.LedgerAllocations.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => a.CurrentAccountId).Distinct().ToListAsync(ct);
        if (caris.Count == 0) throw TreasuryErrors.DifferenceNotFound();
        if (caris.Count > 1) throw TreasuryErrors.AllocationOtherAccount();

        return caris[0];
    }

    /// <summary>Cari kilidi alındıktan sonra çağrılır: tüm tahsisler işlenebilir kur farkı taşımalı ve hiçbiri işlenmiş olmamalıdır.</summary>
    public static async Task<(List<ExchangeDifferenceDto> Items, decimal Total)> LoadAsync(ExchangeDifferenceReader reader,
                                                                                         Guid                      cari,
                                                                                         IReadOnlyList<Guid>       ids,
                                                                                         CancellationToken         ct
    ) {
        var items   = await reader.ListAsync(cari, null, null, ids, true, ct);
        var covered = items.SelectMany(i => i.AllocationIds).ToHashSet();
        if (ids.Any(id => !covered.Contains(id))) throw TreasuryErrors.DifferenceNotFound();

        var settled = await reader.SettledIdsAsync(cari, ct);
        if (ids.Any(settled.Contains)) throw TreasuryErrors.DifferenceAlreadySettled();

        return (items, items.Sum(i => i.Difference));
    }

    public static void Record(IApplicationDbContext db, IOutbox outbox, Guid cari, decimal amount, string resultType, Guid resultId, IReadOnlyList<Guid> ids) {
        db.ExchangeDifferenceSettlements.Add(new ExchangeDifferenceSettlement {
                                                                                  CurrentAccountId = cari,
                                                                                  Amount           = amount,
                                                                                  ResultType       = resultType,
                                                                                  ResultId         = resultId,
                                                                                  AllocationIds    = ids.ToList()
                                                                              });
        outbox.Enqueue("exchange_difference.settled", new { currentAccountId = cari, amount, resultType, resultId, allocationIds = ids });
    }
}
