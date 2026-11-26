using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing.Documents;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Quotes;

[Authorize(Permission = SalesPermissions.Read)]
public sealed record ListQuotesQuery(
    QuoteStatus? Status,
    Guid?        CurrentAccountId,
    Guid?        SalesRepUserId,
    string?      Search,
    int          Page     = 1,
    int          PageSize = 25
) : IRequest<QuoteListResult>;

public sealed class ListQuotesQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<ListQuotesQuery, QuoteListResult> {
    public async Task<QuoteListResult> Handle(ListQuotesQuery request, CancellationToken ct) {
        var query = db.Quotes.AsNoTracking().AsQueryable();
        if (tenant.BranchIds is { } branchList) {
            var branches = branchList.ToList();
            query = query.Where(q => branches.Contains(q.BranchId));
        }
        if (request.Status is { } status) query = query.Where(q => q.Status == status);
        if (request.CurrentAccountId is { } cari) query = query.Where(q => q.CurrentAccountId == cari);
        if (request.SalesRepUserId is { } rep) query = query.Where(q => q.SalesRepUserId == rep);
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(q => q.Number.ToLower().Contains(term)
                                     || db.CurrentAccounts.Any(c => c.Id == q.CurrentAccountId && c.Name.ToLower().Contains(term)));
        }

        var total    = await query.CountAsync(ct);
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var rows = await query.OrderByDescending(q => q.Created).ThenByDescending(q => q.Number).Skip((page - 1) * pageSize).Take(pageSize)
                              .Select(q => new {
                                  q.Id, q.Number, q.Status, q.CurrentAccountId,
                                  CustomerName = db.CurrentAccounts.Where(c => c.Id == q.CurrentAccountId).Select(c => c.Name).FirstOrDefault(),
                                  Revision = q.Revisions.Where(r => r.Revision == q.CurrentRevision).Select(r => new { r.Date, r.ValidUntil, r.Total }).FirstOrDefault(),
                                  q.Currency, q.SalesRepName, q.CurrentRevision
                              }).ToListAsync(ct);

        return new QuoteListResult(rows.Select(r => new QuoteRowDto(r.Id, r.Number, r.Status, r.CurrentAccountId, r.CustomerName ?? string.Empty,
                                                                     r.Revision?.Date ?? default, r.Revision?.ValidUntil, r.Currency, r.Revision?.Total ?? 0m,
                                                                     r.SalesRepName, r.CurrentRevision)).ToList(), total);
    }
}

[Authorize(Permission = SalesPermissions.Read)]
public sealed record GetQuoteQuery(Guid Id, int? Revision) : IRequest<QuoteDto>;

public sealed class GetQuoteQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    QuoteMapper           mapper
) : IRequestHandler<GetQuoteQuery, QuoteDto> {
    public async Task<QuoteDto> Handle(GetQuoteQuery request, CancellationToken ct) {
        var quote = await QuoteRules.LoadAsync(db, tenant, request.Id, false, ct);
        return await mapper.ToDtoAsync(quote, request.Revision, ct);
    }
}

[Authorize(Permission = SalesPermissions.Read)]
public sealed record GetQuotePdfQuery(Guid Id, int? Revision) : IRequest<QuotePdfFile>;

public sealed class GetQuotePdfQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IDocumentPdf          pdf
) : IRequestHandler<GetQuotePdfQuery, QuotePdfFile> {
    public async Task<QuotePdfFile> Handle(GetQuotePdfQuery request, CancellationToken ct) {
        var quote   = await QuoteRules.LoadAsync(db, tenant, request.Id, false, ct);
        var content = await pdf.RenderQuoteAsync(quote.Id, request.Revision, ct);
        return new QuotePdfFile(content, InvoiceMail.FileName(quote.Number, quote.Id));
    }
}
