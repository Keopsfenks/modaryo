using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.Invoicing.Queries;

[Authorize(Permission = InvoicingPermissions.Read)]
public sealed record GetInvoiceQuery(Guid Id) : IRequest<InvoiceDto>;

public sealed class GetInvoiceQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session
) : IRequestHandler<GetInvoiceQuery, InvoiceDto> {
    public async Task<InvoiceDto> Handle(GetInvoiceQuery request, CancellationToken ct) {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).Include(i => i.DueSchedule).FirstOrDefaultAsync(i => i.Id == request.Id, ct)
                      ?? throw new NotFoundException(request.Id.ToString(), "Invoice");
        InvoiceRules.EnsureKindPermission(session, invoice.Kind);
        BranchGuard.Ensure(tenant, invoice.BranchId);
        var name = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == invoice.CurrentAccountId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        return InvoiceRules.ToDto(invoice, name);
    }
}
