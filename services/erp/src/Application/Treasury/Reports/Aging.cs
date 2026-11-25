using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Reports;

public sealed record AgingRowDto(
    Guid                                 CurrentAccountId,
    string                               Code,
    string                               Name,
    string                               Currency,
    IReadOnlyDictionary<string, decimal> Buckets,
    decimal                              Total
);

public sealed record AgingTotalDto(string Currency, IReadOnlyDictionary<string, decimal> Buckets, decimal Total);

public sealed record AgingReportDto(DateOnly AsOf, string Direction, IReadOnlyList<AgingRowDto> Rows, IReadOnlyList<AgingTotalDto> Totals);

/// <summary>
///     Yaşlandırma: açık kalemler vadesine göre gecikme gününe bölünür (current = vadesi gelmemiş, 1-30, 31-60, 61-90, 90+).
///     direction: receivable (alacaklar) | payable (borçlar). Cari ve para birimi başına; genel toplamlar para birimi başınadır (döviz TL'ye çevrilmez).
/// </summary>
[Authorize(Permission = TreasuryPermissions.Read)]
public sealed record AgingReportQuery(string Direction, string? Currency, DateOnly? AsOf) : IRequest<AgingReportDto>;

public sealed class AgingReportQueryValidator : AbstractValidator<AgingReportQuery> {
    public AgingReportQueryValidator() {
        RuleFor(q => q.Direction).Must(d => d is "receivable" or "payable").WithMessage("Yön receivable ya da payable olmalıdır.");
        RuleFor(q => q.Currency).Must(c => c is null || CardRules.Currencies.Contains(c)).WithMessage("Para birimi TRY, USD, EUR veya GBP olmalıdır.");
    }
}

public sealed class AgingReportQueryHandler(
    IApplicationDbContext db,
    OpenItemsReader       reader,
    TimeProvider          time
) : IRequestHandler<AgingReportQuery, AgingReportDto> {
    public async Task<AgingReportDto> Handle(AgingReportQuery request, CancellationToken ct) {
        var asOf      = request.AsOf ?? CurrentAccountClock.Today(time);
        var direction = request.Direction == "receivable" ? DueDirection.Receivable : DueDirection.Payable;

        var caris = db.CurrentAccounts.AsNoTracking().AsQueryable();
        if (request.Currency is { } currency) caris = caris.Where(c => c.Currency == currency);

        var accounts = await caris.OrderBy(c => c.Code).ToListAsync(ct);
        var items    = await reader.ForAccountsAsync(accounts.Select(a => a.Id).ToList(), asOf, ct);

        var rows = new List<AgingRowDto>();
        foreach (var account in accounts) {
            var open = items[account.Id].Where(i => i.Direction == direction && i.Remaining > 0m).ToList();
            if (open.Count == 0) continue;

            var buckets = Empty();
            foreach (var item in open) buckets[AgingBuckets.Of(item.DaysOverdue)] += item.Remaining;

            rows.Add(new AgingRowDto(account.Id, account.Code, account.Name, account.Currency, buckets, buckets.Values.Sum()));
        }

        var totals = rows.GroupBy(r => r.Currency).OrderBy(g => g.Key).Select(g => {
            var buckets = Empty();
            foreach (var row in g)
                foreach (var (key, value) in row.Buckets) buckets[key] += value;

            return new AgingTotalDto(g.Key, buckets, buckets.Values.Sum());
        }).ToList();

        return new AgingReportDto(asOf, request.Direction, rows, totals);
    }

    private static Dictionary<string, decimal> Empty() {
        return AgingBuckets.All.ToDictionary(b => b, _ => 0m);
    }
}
