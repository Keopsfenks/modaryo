using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Common;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Queries;

[Authorize(Permission = InvoicingPermissions.Read)]
public sealed record ListInvoicesQuery(
    InvoiceKind?   Kind,
    InvoiceStatus? Status,
    Guid?          CurrentAccountId,
    DateOnly?      From,
    DateOnly?      To,
    string?        Search,
    Guid?          BranchId,
    int            Page     = 1,
    int            PageSize = 25
) : IRequest<InvoiceListResult>;

public sealed class ListInvoicesQueryValidator : AbstractValidator<ListInvoicesQuery> {
    public ListInvoicesQueryValidator() {
        RuleFor(q => q.Page).InclusiveBetween(1, 100000).WithMessage("Sayfa 1 ile 100000 arasında olmalıdır.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100).WithMessage("Sayfa boyutu 1 ile 100 arasında olmalıdır.");
    }
}

public sealed class ListInvoicesQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session
) : IRequestHandler<ListInvoicesQuery, InvoiceListResult> {
    public async Task<InvoiceListResult> Handle(ListInvoicesQuery request, CancellationToken ct) {
        if (request.Kind is { } kind) InvoiceRules.EnsureKindPermission(session, kind);
        if (request.BranchId is { } branch) BranchGuard.Ensure(tenant, branch);

        var query = db.Invoices.AsNoTracking().AsQueryable();
        // Alış faturaları fiyat.alis olmayan kullanıcıya hiç görünmez.
        if (!session.HasPermission(InvoiceRules.PurchasePermission))
            query = query.Where(i => i.Kind != InvoiceKind.Purchase && i.Kind != InvoiceKind.PurchaseReturn);
        if (tenant.BranchIds is { } allowed) query = query.Where(i => allowed.Contains(i.BranchId));
        if (request.Kind is { } k) query = query.Where(i => i.Kind == k);
        if (request.Status is { } s) query = query.Where(i => i.Status == s);
        if (request.CurrentAccountId is { } cari) query = query.Where(i => i.CurrentAccountId == cari);
        if (request.BranchId is { } b) query = query.Where(i => i.BranchId == b);
        if (request.From is { } from) query = query.Where(i => i.Date >= from);
        if (request.To is { } to) query = query.Where(i => i.Date <= to);

        var rows = query.Join(db.CurrentAccounts.AsNoTracking(), i => i.CurrentAccountId, c => c.Id, (i, c) => new { Invoice = i, c.Name });
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = TurkishText.Fold(request.Search);
            rows = rows.Where(r => (r.Invoice.Number != null && r.Invoice.Number.ToLower().Contains(term))
                                   || (r.Invoice.ExternalNumber != null && r.Invoice.ExternalNumber.ToLower().Contains(term))
                                   || r.Name.ToLower().Replace("ı", "i").Contains(term));
        }

        var total = await rows.CountAsync(ct);
        var sums = await rows.Where(r => r.Invoice.Status != InvoiceStatus.Cancelled)
                       .GroupBy(_ => 1)
                       .Select(g => new { Net = g.Sum(r => r.Invoice.NetTl), Vat = g.Sum(r => r.Invoice.VatTl), Payable = g.Sum(r => r.Invoice.PayableTl) })
                       .FirstOrDefaultAsync(ct);
        var items = await rows.OrderByDescending(r => r.Invoice.Date).ThenByDescending(r => r.Invoice.Created).ThenByDescending(r => r.Invoice.Id)
                        .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
                        .Select(r => new InvoiceListItem(r.Invoice.Id, r.Invoice.Kind, r.Invoice.Status, r.Invoice.Mode, r.Invoice.BranchId, r.Invoice.Number,
                                                         r.Invoice.ExternalNumber, r.Invoice.CurrentAccountId, r.Name, r.Invoice.Date, r.Invoice.Currency,
                                                         r.Invoice.Net, r.Invoice.Vat, r.Invoice.Payable, r.Invoice.PayableTl))
                        .ToListAsync(ct);
        return new InvoiceListResult(items, total, new InvoiceListTotals(sums?.Net ?? 0m, sums?.Vat ?? 0m, sums?.Payable ?? 0m));
    }
}
