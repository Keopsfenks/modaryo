using ERP.Application.Common.Interfaces;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.ExchangeDifferences;

/// <summary>Bir fatura-ödeme çiftinin kur farkı: çifte ait tahsis kimlikleri, döviz tutarı, kurlar ve fark (alacak kuru − borç kuru).</summary>
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

/// <summary>
///     Kur farkı hesabı. TL carilerde, kaynak para birimi aynı yabancı para olan bir borç ve bir alacak hareketi arasındaki her tahsis için
///     döviz tutarı (tahsisle tükenen tarafın kuruyla tahsis tutarı / kur) × (alacak kuru − borç kuru). Fatura tarafı alacaklı (müşteri faturası = borç) ya da
///     borçlu (alış faturası = alacak) olabilir. Fark sıfırsa, iptal edilmiş ya da işlenmiş (<see cref="ExchangeDifferenceSettlement" />) tahsisler listelenmez.
/// </summary>
public sealed class ExchangeDifferenceReader(IApplicationDbContext db) {
    private sealed record Row(
        Guid     AllocationId,
        Guid     CariId,
        string   CariName,
        decimal  Amount,
        Guid     DebitEntryId,
        Guid     CreditEntryId,
        string?  DebitNo,
        string?  CreditNo,
        LedgerEntrySource DebitSource,
        LedgerEntrySource CreditSource,
        string   Currency,
        decimal  DebitRate,
        decimal  CreditRate,
        DateOnly DebitDate,
        DateOnly CreditDate,
        decimal  DebitTl,
        decimal  CreditTl);

    /// <summary>İşlenmiş tahsis kimlikleri (firma içinde, isteğe bağlı cari için).</summary>
    public async Task<HashSet<Guid>> SettledIdsAsync(Guid? cariId, CancellationToken ct) {
        var query = db.ExchangeDifferenceSettlements.AsNoTracking();
        if (cariId is { } id) query = query.Where(s => s.CurrentAccountId == id);

        var lists = await query.Select(s => s.AllocationIds).ToListAsync(ct);
        return lists.SelectMany(l => l).ToHashSet();
    }

    /// <summary>
    ///     Kur farklarını listeler. <paramref name="ids" /> verilirse yalnızca bu tahsisleri kapsar ve tarih aralığı yok sayılır;
    ///     <paramref name="includeSettled" /> false ise işlenmiş tahsisler çıkarılır.
    /// </summary>
    public async Task<List<ExchangeDifferenceDto>> ListAsync(Guid?                       cariId,
                                                             DateOnly?                   from,
                                                             DateOnly?                   to,
                                                             IReadOnlyCollection<Guid>?  ids,
                                                             bool                        includeSettled,
                                                             CancellationToken           ct
    ) {
        var allocations = db.LedgerAllocations.AsNoTracking();
        if (cariId is { } cari) allocations = allocations.Where(a => a.CurrentAccountId == cari);
        if (ids is not null) allocations = allocations.Where(a => ids.Contains(a.Id));

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
        if (ids is null) {
            if (from is { } f) rows = rows.Where(r => r.DebitDate >= f || r.CreditDate >= f).ToList();
            if (to is { } t) rows = rows.Where(r => r.DebitDate <= t && r.CreditDate <= t).ToList();
        }

        if (!includeSettled) {
            var settled = await SettledIdsAsync(cariId, ct);
            rows = rows.Where(r => !settled.Contains(r.AllocationId)).ToList();
        }

        var result = new List<ExchangeDifferenceDto>();
        foreach (var group in rows.GroupBy(r => (r.DebitEntryId, r.CreditEntryId))) {
            var first = group.First();
            // Fatura tarafı: borç hareketi fatura ise alacaklı (müşteri), alacak hareketi fatura ise borçlu (tedarikçi) taraf.
            var receivable = first.DebitSource == LedgerEntrySource.Invoice && first.CreditSource != LedgerEntrySource.Invoice;
            var payable    = first.CreditSource == LedgerEntrySource.Invoice && first.DebitSource != LedgerEntrySource.Invoice;
            if (!receivable && !payable) continue;

            var invoiceRate = receivable ? first.DebitRate : first.CreditRate;
            var paymentRate = receivable ? first.CreditRate : first.DebitRate;
            // Döviz tutarı: TL karşılığı küçük olan taraf tahsisle tükenen taraftır; döviz o tarafın kuruyla bulunur
            // (yüksek kurla ödenen tam fatura = faturanın döviz tutarı; düşük kurla ödenen ödeme = ödemenin döviz tutarı).
            var settleRate  = first.DebitTl <= first.CreditTl ? first.DebitRate : first.CreditRate;
            var foreign     = group.Sum(r => r.Amount / settleRate);
            var difference  = KurFarki.Compute(foreign, first.DebitRate, first.CreditRate);
            if (difference == 0m) continue;

            result.Add(new ExchangeDifferenceDto(first.CariId, first.CariName, receivable ? "receivable" : "payable", group.Select(r => r.AllocationId).ToList(),
                                                 (receivable ? first.DebitNo : first.CreditNo) ?? string.Empty, Math.Round(foreign, 2, MidpointRounding.AwayFromZero),
                                                 first.Currency, invoiceRate, paymentRate, difference));
        }

        return result.OrderBy(d => d.CurrentAccountName).ThenBy(d => d.InvoiceNumber, StringComparer.Ordinal).ToList();
    }
}
