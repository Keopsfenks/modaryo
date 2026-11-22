using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Queries;

[Authorize(Permission = CariPermissions.Read)]
public sealed record ListCurrentAccountsQuery(string? Search, string? Role, bool IncludeInactive, int Page = 1, int PageSize = 25)
    : IRequest<PagedResult<CurrentAccountListItem>>;

public sealed class ListCurrentAccountsQueryValidator : AbstractValidator<ListCurrentAccountsQuery>
{
    public ListCurrentAccountsQueryValidator()
    {
        RuleFor(q => q.Role).Must(r => r is "customer" or "supplier").When(q => !string.IsNullOrEmpty(q.Role))
            .WithMessage("Rol müşteri (customer) veya tedarikçi (supplier) olmalıdır.");
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).WithMessage("Sayfa 1 veya daha büyük olmalıdır.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100).WithMessage("Sayfa boyutu 1 ile 100 arasında olmalıdır.");
    }
}

public sealed class ListCurrentAccountsQueryHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<ListCurrentAccountsQuery, PagedResult<CurrentAccountListItem>>
{
    public async Task<PagedResult<CurrentAccountListItem>> Handle(ListCurrentAccountsQuery request, CancellationToken ct)
    {
        var query = db.CurrentAccounts.AsNoTracking().AsQueryable();

        if (!request.IncludeInactive)
        {
            query = query.Where(a => a.IsActive);
        }

        if (request.Role == "customer")
        {
            query = query.Where(a => a.IsCustomer);
        }
        else if (request.Role == "supplier")
        {
            query = query.Where(a => a.IsSupplier);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(a => a.Name.ToLower().Contains(term) || a.Code.ToLower().Contains(term)
                || (a.TaxNumber != null && a.TaxNumber.Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var page = await query.OrderBy(a => a.Code).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);

        var ids = page.Select(a => a.Id).ToList();
        var entries = (await db.LedgerEntries.AsNoTracking().Where(e => ids.Contains(e.CurrentAccountId)).ToListAsync(ct))
            .ToLookup(e => e.CurrentAccountId);
        var today = CurrentAccountClock.Today(time);

        var items = page.Select(a =>
        {
            var own = entries[a.Id].ToList();
            var balance = Statement.Balance(own);
            return new CurrentAccountListItem(a.Id, a.Code, a.Name, a.IsCustomer, a.IsSupplier, a.Currency, balance,
                a.CreditLimit is not null && balance > a.CreditLimit, CurrentAccountReader.Overdue(own, today), a.IsActive);
        }).ToList();

        return new PagedResult<CurrentAccountListItem>(items, total);
    }
}
