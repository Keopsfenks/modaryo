using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Queries;

public sealed record DueBucket(decimal Overdue, decimal Within30, decimal Later);

public sealed record CurrencyDueSummary(string Currency, DueBucket Receivable, DueBucket Payable);

public sealed record OverdueAccount(Guid Id, string Code, string Name, string Currency, decimal OverdueReceivable, int MaxDaysOverdue);

public sealed record DueSummary(IReadOnlyList<CurrencyDueSummary> ByCurrency, IReadOnlyList<OverdueAccount> TopOverdue);

[Authorize(Permission = CariPermissions.Read)]
public sealed record GetDueSummaryQuery : IRequest<DueSummary>;

public sealed class GetDueSummaryQueryHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<GetDueSummaryQuery, DueSummary>
{
    private const int TopCount = 20;

    public async Task<DueSummary> Handle(GetDueSummaryQuery request, CancellationToken ct)
    {
        var today = CurrentAccountClock.Today(time);
        var limit = today.AddDays(30);

        // Pasif kartlar dahil; tenant filtresi kart ve hareketlere uygulanır.
        var accounts = await db.CurrentAccounts.AsNoTracking().ToListAsync(ct);
        var entries = (await db.LedgerEntries.AsNoTracking().Where(e => e.CancelledAt == null).ToListAsync(ct))
            .ToLookup(e => e.CurrentAccountId);

        var perAccount = accounts
            .Select(a => (Account: a, Items: DueMatching.OpenItems(entries[a.Id], today)))
            .ToList();

        DueBucket Bucket(IEnumerable<OpenItem> items)
        {
            var list = items.ToList();
            return new DueBucket(
                list.Where(i => i.DueDate < today).Sum(i => i.Remaining),
                list.Where(i => i.DueDate >= today && i.DueDate <= limit).Sum(i => i.Remaining),
                list.Where(i => i.DueDate > limit).Sum(i => i.Remaining));
        }

        var byCurrency = perAccount
            .GroupBy(p => p.Account.Currency)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var items = g.SelectMany(p => p.Items).ToList();
                return new CurrencyDueSummary(
                    g.Key,
                    Bucket(items.Where(i => i.Direction == DueDirection.Receivable)),
                    Bucket(items.Where(i => i.Direction == DueDirection.Payable)));
            })
            .ToList();

        var top = perAccount
            .Select(p =>
            {
                var overdue = p.Items.Where(i => i.Direction == DueDirection.Receivable && i.DueDate < today).ToList();
                return (p.Account, Overdue: overdue.Sum(i => i.Remaining), Days: overdue.Count == 0 ? 0 : overdue.Max(i => i.DaysOverdue));
            })
            .Where(x => x.Overdue > 0)
            .OrderByDescending(x => x.Overdue)
            .ThenBy(x => x.Account.Code, StringComparer.Ordinal)
            .Take(TopCount)
            .Select(x => new OverdueAccount(x.Account.Id, x.Account.Code, x.Account.Name, x.Account.Currency, x.Overdue, x.Days))
            .ToList();

        return new DueSummary(byCurrency, top);
    }
}
