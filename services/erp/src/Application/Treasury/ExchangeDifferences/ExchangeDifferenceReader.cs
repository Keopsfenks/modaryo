using ERP.Application.Common.Interfaces;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.ExchangeDifferences;

/// <summary>Bir fatura hareketinin işlenmemiş kur farkı: faturaya ait tahsis kimlikleri, döviz tutarı, kurlar ve fark (alacak kuru - borç kuru).</summary>
public sealed record ExchangeDifferenceDto(
    Guid               CurrentAccountId,
    string             CurrentAccountName,
    string             Side,
    IReadOnlyList<Guid> AllocationIds,
    string             InvoiceNumber,
    decimal            ForeignAmount,
    string             Currency,
    decimal            InvoiceRate,
    decimal            PaymentRate,
    decimal            Difference
);

/// <summary>Bir fatura hareketinin kur farkı satırı: işlenmiş (<see cref="Settled" />) ve işlenmemiş (<see cref="Dto" />.Difference) kısımlarıyla.</summary>
internal sealed record DifferenceRow(ExchangeDifferenceDto Dto, Guid InvoiceEntryId, decimal Total, decimal Settled) {
    public decimal Unsettled => Dto.Difference;

    public bool Receivable => Dto.Side == "receivable";
}

/// <summary>
///     Kur farkı hesabı. TL carilerde, kaynak para birimi aynı yabancı para olan bir borç ve bir alacak hareketi arasındaki her tahsis için
///     döviz tutarı (tahsisle tükenen tarafın kuruyla tahsis tutarı / kur) × (alacak kuru - borç kuru). Fatura tarafı alacaklı (müşteri faturası = borç) ya da
///     borçlu (alış faturası = alacak) olabilir.
///     İşlenmiş kısım fatura hareketi bazında tutulur (<see cref="ExchangeDifferenceSettlementItem" />): satırın işlenmemiş farkı = toplam fark - işlenmiş tutar.
///     Tahsis kimlikleri iptal, yeniden giriş ve FIFO kaymasıyla değişse de fatura hareketi sabit kaldığından aynı fark ikinci kez çıkmaz;
///     eşleşme gerçekten farklılaşırsa yalnızca işlenmemiş fark listelenir. İptal edilmiş hareketler ve sıfır farklar listelenmez.
/// </summary>
public sealed class ExchangeDifferenceReader(IApplicationDbContext db) {
    private sealed record Row(
        Guid              AllocationId,
        Guid              CariId,
        string            CariName,
        decimal           Amount,
        Guid              DebitEntryId,
        Guid              CreditEntryId,
        string?           DebitNo,
        string?           CreditNo,
        LedgerEntrySource DebitSource,
        LedgerEntrySource CreditSource,
        string            Currency,
        decimal           DebitRate,
        decimal           CreditRate,
        DateOnly          DebitDate,
        DateOnly          CreditDate,
        decimal           DebitTl,
        decimal           CreditTl);

    /// <summary>
    ///     Kur farklarını listeler: işlenmemiş kısmı kalanlar. <paramref name="ids" /> verilirse yalnızca bu tahsisleri kapsayan satırlar gelir ve tarih aralığı yok sayılır;
    ///     <paramref name="includeSettled" /> true ise tümüyle işlenmiş satırlar da (işlenmemiş fark 0) döner.
    /// </summary>
    public async Task<List<ExchangeDifferenceDto>> ListAsync(Guid? cariId, DateOnly? from, DateOnly? to, CancellationToken ct) {
        return (await RowsAsync(cariId, from, to, null, false, ct)).Select(r => r.Dto).ToList();
    }

    internal async Task<List<DifferenceRow>> RowsAsync(Guid?                      cariId,
                                                       DateOnly?                  from,
                                                       DateOnly?                  to,
                                                       IReadOnlyCollection<Guid>? ids,
                                                       bool                       includeSettled,
                                                       CancellationToken          ct
    ) {
        var allocations = db.LedgerAllocations.AsNoTracking();
        if (cariId is { } cari) allocations = allocations.Where(a => a.CurrentAccountId == cari);

        var query = from a in allocations
                    join dl in db.LedgerDueLines.AsNoTracking() on a.DebitLineId equals dl.Id
                    join cl in db.LedgerDueLines.AsNoTracking() on a.CreditLineId equals cl.Id
                    join de in db.LedgerEntries.AsNoTracking() on dl.EntryId equals de.Id
                    join ce in db.LedgerEntries.AsNoTracking() on cl.EntryId equals ce.Id
                    join c in db.CurrentAccounts.AsNoTracking() on a.CurrentAccountId equals c.Id
                    where c.Currency == "TRY"
                          && de.CancelledAt == null && ce.CancelledAt == null
                          && de.SourceCurrency != null && de.SourceCurrency != "TRY" && de.SourceCurrency == ce.SourceCurrency
                          && de.Rate > 0m && ce.Rate > 0m
                    select new Row(a.Id, c.Id, c.Name, a.Amount, de.Id, ce.Id, de.DocumentNo, ce.DocumentNo, de.Source, ce.Source, de.SourceCurrency!,
                                   de.Rate!.Value, ce.Rate!.Value, de.Date, ce.Date, de.Debit, ce.Credit);
        var rows = await query.ToListAsync(ct);

        var settledByEntry = (await db.ExchangeDifferenceSettlementItems.AsNoTracking().Select(i => new { i.InvoiceEntryId, i.Amount }).ToListAsync(ct))
                             .GroupBy(i => i.InvoiceEntryId).ToDictionary(g => g.Key, g => g.Sum(i => i.Amount));

        var result = new List<DifferenceRow>();
        // Kur farkı fatura hareketi başına toplanır: aynı faturayı kapatan birden çok tahsilat tek satırda birleşir.
        var byInvoice = rows.Where(r => IsInvoiceSide(r) is not null).GroupBy(r => IsInvoiceSide(r)! == true ? r.DebitEntryId : r.CreditEntryId);
        foreach (var invoice in byInvoice) {
            var members = invoice.ToList();
            if (ids is not null && !members.Any(m => ids.Contains(m.AllocationId))) continue;

            if (ids is null) {
                if (from is { } f && !members.Any(m => m.DebitDate >= f || m.CreditDate >= f)) continue;
                if (to is { } t && !members.All(m => m.DebitDate <= t && m.CreditDate <= t)) continue;
            }

            var first      = members[0];
            var receivable = IsInvoiceSide(first)!.Value;

            // Çift (borç hareketi, alacak hareketi) başına: tahsisle tükenen taraf (TL karşılığı küçük olan) kurundan döviz tutarı; fark = döviz x (alacak kuru - borç kuru).
            decimal foreign = 0m, difference = 0m, weighted = 0m;
            foreach (var pair in members.GroupBy(m => (m.DebitEntryId, m.CreditEntryId))) {
                var head       = pair.First();
                var settleRate = head.DebitTl <= head.CreditTl ? head.DebitRate : head.CreditRate;
                var pairForeign = pair.Sum(m => m.Amount / settleRate);
                var paymentRate = receivable ? head.CreditRate : head.DebitRate;
                foreign    += pairForeign;
                weighted   += pairForeign * paymentRate;
                difference += KurFarki.Compute(pairForeign, head.DebitRate, head.CreditRate);
            }

            difference = Math.Round(difference, 2, MidpointRounding.AwayFromZero);
            if (difference == 0m) continue;

            var invoiceEntryId = receivable ? first.DebitEntryId : first.CreditEntryId;
            var settled        = settledByEntry.GetValueOrDefault(invoiceEntryId);
            var unsettled      = Math.Round(difference - settled, 2, MidpointRounding.AwayFromZero);
            // İşlenmiş tutar fazlaysa (ödeme sonradan düşmüş) ya da farkın işareti dönmüşse işlenecek bir şey yoktur.
            if (Math.Sign(unsettled) != Math.Sign(difference)) unsettled = 0m;
            if (unsettled == 0m && !includeSettled) continue;

            var share        = difference == 0m ? 1m : unsettled / difference;
            var invoiceRate  = receivable ? first.DebitRate : first.CreditRate;
            var invoiceNo    = (receivable ? first.DebitNo : first.CreditNo) ?? string.Empty;
            var dto = new ExchangeDifferenceDto(first.CariId, first.CariName, receivable ? "receivable" : "payable", members.Select(m => m.AllocationId).ToList(), invoiceNo,
                                                Math.Round(foreign * share, 2, MidpointRounding.AwayFromZero), first.Currency, invoiceRate,
                                                foreign == 0m ? 0m : Math.Round(weighted / foreign, 6, MidpointRounding.AwayFromZero), unsettled);
            result.Add(new DifferenceRow(dto, invoiceEntryId, difference, settled));
        }

        return result.OrderBy(r => r.Dto.CurrentAccountName).ThenBy(r => r.Dto.InvoiceNumber, StringComparer.Ordinal).ToList();
    }

    /// <summary>Fatura tarafı: borç hareketi fatura ise alacaklı (müşteri; true), alacak hareketi fatura ise borçlu (tedarikçi; false) taraf; fatura-ödeme çifti değilse null.</summary>
    private static bool? IsInvoiceSide(Row r) {
        if (r.DebitSource == LedgerEntrySource.Invoice && r.CreditSource != LedgerEntrySource.Invoice) return true;
        if (r.CreditSource == LedgerEntrySource.Invoice && r.DebitSource != LedgerEntrySource.Invoice) return false;

        return null;
    }
}
