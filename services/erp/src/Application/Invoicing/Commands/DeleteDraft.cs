using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Treasury.ExchangeDifferences.Commands;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Commands;

[Authorize(Permission = InvoicingPermissions.Manage)]
public sealed record DeleteDraftCommand(Guid Id) : IRequest;

public sealed class DeleteDraftCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session
) : IRequestHandler<DeleteDraftCommand> {
    public async Task Handle(DeleteDraftCommand request, CancellationToken ct) {
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.DueSchedule).FirstOrDefaultAsync(i => i.Id == request.Id, ct)
                      ?? throw new NotFoundException(request.Id.ToString(), "Invoice");
        InvoiceRules.EnsureKindPermission(session, invoice.Kind);
        BranchGuard.Ensure(tenant, invoice.BranchId);
        if (invoice.Status != InvoiceStatus.Draft) throw InvoiceErrors.NotDraft(invoice.Status);

        // İade belgesinden gelen taslak silinemez: stok girişi belgededir; iade belgesi iptal edilir (taslak onunla silinir).
        if (invoice.Kind == InvoiceKind.SalesReturn && invoice.SourceType == "sales-return")
            throw InvoiceErrors.Conflict("return_invoice_locked", "İade belgesinden gelen taslak silinemez; iade belgesini iptal edin.");

        // Silinen kur farkı faturası taslağı işlenmiş kur farkını serbest bırakır (aynı kaydetmede).
        if (invoice.SourceType == "kur-farki") await Settlement.ReleaseAsync(db, invoice.Id, ct);

        db.Invoices.Remove(invoice);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw InvoiceErrors.NotDraft(InvoiceStatus.Issued);
        }
    }
}
