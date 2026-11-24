using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Queries;

[Authorize(Permission = CariPermissions.Read)]
public sealed record ListCurrentAccountsQuery(
    string? Search,
    string? Role,
    bool    IncludeInactive,
    int     Page     = 1,
    int     PageSize = 25
)
    : IRequest<PagedResult<CurrentAccountListItem>>;

public sealed class ListCurrentAccountsQueryValidator : AbstractValidator<ListCurrentAccountsQuery> {
    public ListCurrentAccountsQueryValidator() {
        RuleFor(q => q.Role).Must(r => r is "customer" or "supplier" or "employee").When(q => !string.IsNullOrEmpty(q.Role))
            .WithMessage("Rol müşteri (customer), tedarikçi (supplier) veya personel (employee) olmalıdır.");
        RuleFor(q => q.Page).InclusiveBetween(1, 100000).WithMessage("Sayfa 1 ile 100000 arasında olmalıdır.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100).WithMessage("Sayfa boyutu 1 ile 100 arasında olmalıdır.");
    }
}

public sealed class ListCurrentAccountsQueryHandler(
    IApplicationDbContext db,
    OpenItemsReader       reader,
    TimeProvider          time
)
    : IRequestHandler<ListCurrentAccountsQuery, PagedResult<CurrentAccountListItem>> {
    public async Task<PagedResult<CurrentAccountListItem>> Handle(ListCurrentAccountsQuery request,
                                                                  CancellationToken        ct
    ) {
        var query = db.CurrentAccounts.AsNoTracking().AsQueryable();

        if (!request.IncludeInactive) query = query.Where(a => a.IsActive);

        // Personel carileri müşteri ve tedarikçi listelerinde görünmez; yalnızca personel rolüyle listelenir.
        if (request.Role == "customer")
            query = query.Where(a => a.IsCustomer && !a.IsEmployee);
        else if (request.Role == "supplier")
            query = query.Where(a => a.IsSupplier && !a.IsEmployee);
        else if (request.Role == "employee")
            query = query.Where(a => a.IsEmployee);

        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = CardRules.FoldSearch(request.Search);
            query = query.Where(a => a.Name.ToLower().Replace("ı", "i").Contains(term)
                                     || a.Code.ToLower().Replace("ı", "i").Contains(term)
                                     || (a.TaxNumber != null && a.TaxNumber.Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var page = await query.OrderBy(a => a.Code).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
                       .ToListAsync(ct);

        var ids = page.Select(a => a.Id).ToList();
        var entries = (await db.LedgerEntries.AsNoTracking().Where(e => ids.Contains(e.CurrentAccountId))
                           .ToListAsync(ct))
            .ToLookup(e => e.CurrentAccountId);
        var today     = CurrentAccountClock.Today(time);
        var openItems = await reader.ForAccountsAsync(ids, today, ct);

        var items = page.Select(a => {
                                    var own     = entries[a.Id].ToList();
                                    var balance = Statement.Balance(own);
                                    return new CurrentAccountListItem(a.Id, a.Code, a.Name, a.IsCustomer, a.IsSupplier, a.IsEmployee,
                                                                      a.Currency, balance,
                                                                      a.CreditLimit is not null &&
                                                                      balance > a.CreditLimit,
                                                                      CurrentAccountReader.Overdue(openItems[a.Id]),
                                                                      a.IsActive);
                                }).ToList();

        return new PagedResult<CurrentAccountListItem>(items, total);
    }
}