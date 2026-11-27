using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Reports;

/// <summary>Kaynağa göre tutar (TL): açık vade satırları, açık çek/senetler, henüz üretilmemiş tekrarlayan fatura dönemleri (yalnızca giriş).</summary>
public sealed record CashFlowSources(decimal DueLines, decimal Instruments, decimal Recurring) {
    public decimal Total => DueLines + Instruments + Recurring;
}

public sealed record CashFlowPeriodDto(DateOnly Start, DateOnly End, CashFlowSources Inflow, CashFlowSources Outflow, decimal Net, decimal Closing);

public sealed record CashFlowReportDto(DateOnly Start, string Granularity, string Currency, decimal Opening, IReadOnlyList<CashFlowPeriodDto> Periods, decimal Closing);

/// <summary>
///     Nakit akışı (TL): açılış = kasa + banka hesap bakiyeleri toplamı (döviz başlangıç gününün kuruyla; pos hesapları dahil değildir).
///     Girişler: açık fatura alacak vade satırları ve alınan çek/senetler (portföyde ya da tahsilde). Çıkışlar: açık fatura borç vade satırları ve verilen çek/senetler (ödenmemiş).
///     Faturasız vade satırları (eşleşmemiş avans/ön ödeme, tahsilat/ödeme, çek/senet kayıtları, mahsup) beklenen nakit değildir ve hariç tutulur.
///     Vadesi geçmiş kalemler ilk döneme yazılır; ufku aşanlar dışarıda kalır. granularity: day (en çok 90) | week (en çok 26); from verilmezse bugün.
/// </summary>
[Authorize(Permission = TreasuryPermissions.Read)]
public sealed record CashFlowQuery(string Granularity, int? Periods, DateOnly? From) : IRequest<CashFlowReportDto>;

public sealed class CashFlowQueryValidator : AbstractValidator<CashFlowQuery> {
    public CashFlowQueryValidator() {
        RuleFor(q => q.Granularity).Must(g => g is "day" or "week").WithMessage("Dönem türü day ya da week olmalıdır.");
        RuleFor(q => q.Periods).Must((q, p) => p is null || (p >= 1 && p <= (q.Granularity == "week" ? 26 : 90)))
            .WithMessage("Dönem sayısı en az 1, en çok 26 hafta ya da 90 gün olabilir.");
    }
}

public sealed class CashFlowQueryHandler(
    IApplicationDbContext db,
    OpenItemsReader       reader,
    IExchangeRates        rates,
    ITenantSettings       settings,
    IRecurringForecast    recurring,
    TimeProvider          time
) : IRequestHandler<CashFlowQuery, CashFlowReportDto> {
    public async Task<CashFlowReportDto> Handle(CashFlowQuery request, CancellationToken ct) {
        var start   = request.From ?? CurrentAccountClock.Today(time);
        var weekly  = request.Granularity == "week";
        var count   = request.Periods ?? (weekly ? 12 : 30);
        var step    = weekly ? 7 : 1;
        var horizon = start.AddDays(count * step - 1);
        var type    = (await settings.GetAsync<FinanceSettings>(ct)).RateType;
        var cache   = new Dictionary<string, decimal> { ["TRY"] = 1m };

        async Task<decimal> Tl(decimal amount, string currency) {
            if (!cache.TryGetValue(currency, out var rate)) cache[currency] = rate = (await rates.GetAsync(currency, start, type, ct)).Rate;

            return Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero);
        }

        // Açılış: kasa + banka bakiyeleri.
        var balances = await (from a in db.TreasuryAccounts.AsNoTracking()
                              where a.Kind == TreasuryAccountKind.Cash || a.Kind == TreasuryAccountKind.Bank
                              select new { a.Currency, Balance = db.TreasuryMovements.Where(m => m.AccountId == a.Id && m.CancelledAt == null).Sum(m => (decimal?)m.Amount) ?? 0m })
                           .ToListAsync(ct);
        var opening = 0m;
        foreach (var balance in balances) opening += await Tl(balance.Balance, balance.Currency);

        var dueIn  = new decimal[count];
        var dueOut = new decimal[count];
        var insIn  = new decimal[count];
        var insOut = new decimal[count];
        var recIn  = new decimal[count];

        // Gecikmiş kalemler ilk döneme; ufuktan sonrası dışarıda.
        int? Slot(DateOnly due) {
            if (due > horizon) return null;

            return due <= start ? 0 : (due.DayNumber - start.DayNumber) / step;
        }

        var accounts = await db.CurrentAccounts.AsNoTracking().Select(a => new { a.Id, a.Currency }).ToListAsync(ct);
        var items    = await reader.ForAccountsAsync(accounts.Select(a => a.Id).ToList(), start, ct);

        // Yalnızca fatura vadeleri beklenen nakittir. Tahsilat/ödeme (avans, ön ödeme), çek/senet alım-ciro-keşide satırları ve mahsup/elle
        // hareketler ya açılış bakiyesinde ya da çek/senet listesinde zaten vardır; vade satırı olarak sayılırlarsa çift sayılır.
        var invoiceEntries = (await db.LedgerEntries.AsNoTracking().Where(e => e.Source == LedgerEntrySource.Invoice && e.CancelledAt == null).Select(e => e.Id).ToListAsync(ct)).ToHashSet();
        foreach (var account in accounts)
            foreach (var item in items[account.Id].Where(i => i.Remaining > 0m && invoiceEntries.Contains(i.EntryId))) {
                if (Slot(item.DueDate) is not { } slot) continue;

                var tl = await Tl(item.Remaining, account.Currency);
                if (item.Direction == DueDirection.Receivable) dueIn[slot] += tl;
                else dueOut[slot]                                          += tl;
            }

        var open = await db.Instruments.AsNoTracking()
                       .Where(i => i.Status == InstrumentStatus.InPortfolio || i.Status == InstrumentStatus.InCollection || i.Status == InstrumentStatus.Issued)
                       .Select(i => new { i.Direction, i.DueDate, i.Amount, i.Currency }).ToListAsync(ct);
        foreach (var instrument in open) {
            if (Slot(instrument.DueDate) is not { } slot) continue;

            var tl = await Tl(instrument.Amount, instrument.Currency);
            if (instrument.Direction == InstrumentDirection.Received) insIn[slot] += tl;
            else insOut[slot]                                                    += tl;
        }

        // Tekrarlayan faturalar: yalnızca üretilmemiş gelecek dönemler (üretilenler fatura vadesi olarak yukarıda sayılır).
        foreach (var item in await recurring.UpcomingAsync(start, horizon, ct)) {
            if (Slot(item.Date) is not { } slot) continue;

            recIn[slot] += await Tl(item.Amount, item.Currency);
        }

        var periods = new List<CashFlowPeriodDto>();
        var closing = opening;
        for (var i = 0; i < count; i++) {
            var inflow  = new CashFlowSources(dueIn[i], insIn[i], recIn[i]);
            var outflow = new CashFlowSources(dueOut[i], insOut[i], 0m);
            var net     = inflow.Total - outflow.Total;
            closing += net;
            periods.Add(new CashFlowPeriodDto(start.AddDays(i * step), start.AddDays(i * step + step - 1), inflow, outflow, net, closing));
        }

        return new CashFlowReportDto(start, request.Granularity, "TRY", opening, periods, closing);
    }
}
