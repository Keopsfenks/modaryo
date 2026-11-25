using ERP.Application.Common.Interfaces;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.ExchangeDifferences.Commands;

/// <summary>Seçilen kur farklarının işlem türü: fatura (alacak tarafı, pozitif), gelir fişi (borç tarafı, pozitif), gider fişi (negatif).</summary>
internal enum DifferenceKind { Invoice, Income, Expense }

/// <summary>Kur farkı işlemlerinin ortak adımları: cari tespiti, kilit sonrası kontrol, işlenmiş kaydı ve serbest bırakma.</summary>
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

    /// <summary>
    ///     Cari kilidi alındıktan sonra çağrılır: seçilen tahsisleri kapsayan satırlar (fatura hareketi başına) işlenebilir kur farkı taşımalı,
    ///     hiçbiri tümüyle işlenmiş olmamalı ve hepsi aynı türde (fatura / gelir / gider) olmalıdır. Toplam = işlenmemiş farkların toplamı.
    /// </summary>
    public static async Task<(List<DifferenceRow> Rows, decimal Total, DifferenceKind Kind)> LoadAsync(ExchangeDifferenceReader reader,
                                                                                                    Guid                      cari,
                                                                                                    IReadOnlyList<Guid>       ids,
                                                                                                    CancellationToken         ct
    ) {
        var rows    = await reader.RowsAsync(cari, null, null, ids, true, ct);
        var covered = rows.SelectMany(r => r.Dto.AllocationIds).ToHashSet();
        if (ids.Any(id => !covered.Contains(id))) throw TreasuryErrors.DifferenceNotFound();
        if (rows.Any(r => r.Unsettled == 0m)) throw TreasuryErrors.DifferenceAlreadySettled();

        var kinds = rows.Select(KindOf).Distinct().ToList();
        if (kinds.Count > 1) throw TreasuryErrors.DifferenceNotApplicable("Alacak/borç yönü ya da işareti farklı kur farkları birlikte işlenemez; ayrı ayrı seçin.");

        return (rows, rows.Sum(r => r.Unsettled), kinds[0]);
    }

    /// <summary>
    ///     Cari artığını kapatan belge: alacak tarafında pozitif fark (müşteri fazla ödedi, cari alacak artığı) için kur farkı faturası (cariyi borçlandırır);
    ///     borç tarafında pozitif fark (alış faturası yüksek kurdan yazılmış, cari alacak artığı) için kur farkı geliri fişi (cariyi borçlandırır);
    ///     negatif fark (cari borç artığı) için kur farkı gideri fişi (cariyi alacaklandırır).
    /// </summary>
    private static DifferenceKind KindOf(DifferenceRow row) {
        if (row.Unsettled < 0m) return DifferenceKind.Expense;

        return row.Receivable ? DifferenceKind.Invoice : DifferenceKind.Income;
    }

    public static void Record(IApplicationDbContext db, IOutbox outbox, Guid cari, decimal amount, string resultType, Guid resultId, IReadOnlyList<Guid> ids, IReadOnlyList<DifferenceRow> rows) {
        var settlement = new ExchangeDifferenceSettlement {
                                                              CurrentAccountId = cari,
                                                              Amount           = amount,
                                                              ResultType       = resultType,
                                                              ResultId         = resultId,
                                                              AllocationIds    = ids.ToList()
                                                          };
        db.ExchangeDifferenceSettlements.Add(settlement);
        foreach (var row in rows)
            db.ExchangeDifferenceSettlementItems.Add(new ExchangeDifferenceSettlementItem { SettlementId = settlement.Id, InvoiceEntryId = row.InvoiceEntryId, Amount = row.Unsettled });

        outbox.Enqueue("exchange_difference.settled", new { currentAccountId = cari, amount, resultType, resultId, allocationIds = ids });
    }

    /// <summary>
    ///     Sonuç belgesi (kur farkı faturası ya da gider/gelir fişi) iptal edildiğinde ya da taslak fatura silindiğinde işlenmiş kaydı bırakır: fark yeniden işlenebilir.
    ///     Çağıranın işlemi içinde, SaveChanges'ten önce çağrılır.
    /// </summary>
    public static async Task ReleaseAsync(IApplicationDbContext db, Guid resultId, CancellationToken ct) {
        var settlements = await db.ExchangeDifferenceSettlements.Where(s => s.ResultId == resultId).ToListAsync(ct);
        if (settlements.Count == 0) return;

        var ids   = settlements.Select(s => s.Id).ToList();
        var items = await db.ExchangeDifferenceSettlementItems.Where(i => ids.Contains(i.SettlementId)).ToListAsync(ct);
        db.ExchangeDifferenceSettlementItems.RemoveRange(items);
        db.ExchangeDifferenceSettlements.RemoveRange(settlements);
    }
}
