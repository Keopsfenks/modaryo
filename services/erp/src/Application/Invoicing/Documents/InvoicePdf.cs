using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.Invoicing.Documents;

[Authorize(Permission = InvoicingPermissions.Read)]
public sealed record GetInvoicePdfQuery(Guid Id) : IRequest<InvoicePdfFile>;

public sealed record InvoicePdfFile(byte[] Content, string FileName);

public sealed class GetInvoicePdfQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    IDocumentPdf          pdf
) : IRequestHandler<GetInvoicePdfQuery, InvoicePdfFile> {
    public async Task<InvoicePdfFile> Handle(GetInvoicePdfQuery request, CancellationToken ct) {
        var head = await db.Invoices.AsNoTracking().Where(i => i.Id == request.Id).Select(i => new { i.Kind, i.BranchId, i.Number }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "Invoice");
        InvoiceRules.EnsureKindPermission(session, head.Kind);
        BranchGuard.Ensure(tenant, head.BranchId);

        return new InvoicePdfFile(await pdf.RenderInvoiceAsync(request.Id, ct), InvoiceMail.FileName(head.Number, request.Id));
    }
}
