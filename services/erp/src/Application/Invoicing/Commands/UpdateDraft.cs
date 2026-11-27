using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Purchasing.Invoices;
using ERP.Application.Sales.Invoicing;
using ERP.Application.Sales.Returns;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Commands;

[Authorize(Permission = InvoicingPermissions.Manage)]
public sealed record UpdateDraftCommand(Guid Id, InvoiceInput Input) : IRequest<InvoiceDto>;

public sealed class UpdateDraftCommandValidator : AbstractValidator<UpdateDraftCommand> {
    public UpdateDraftCommandValidator() {
        RuleFor(c => c.Input).NotNull().SetValidator(new InvoiceInputValidator());
    }
}

public sealed class UpdateDraftCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    InvoiceBuilder        builder,
    ReceiptInvoiceGuard   receiptGuard,
    ShipmentInvoiceGuard  shipmentGuard,
    ReturnService         returns,
    TimeProvider          clock
) : IRequestHandler<UpdateDraftCommand, InvoiceDto> {
    public async Task<InvoiceDto> Handle(UpdateDraftCommand request, CancellationToken ct) {
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.DueSchedule).FirstOrDefaultAsync(i => i.Id == request.Id, ct)
                      ?? throw new NotFoundException(request.Id.ToString(), "Invoice");
        InvoiceRules.EnsureKindPermission(session, invoice.Kind);
        BranchGuard.Ensure(tenant, invoice.BranchId);
        if (invoice.Status != InvoiceStatus.Draft) throw InvoiceErrors.NotDraft(invoice.Status);
        if (request.Input.Version is { } version && version != invoice.Version) throw InvoiceErrors.VersionConflict();

        var input = request.Input;
        var linked = ReceiptInvoiceGuard.IsReceiptLinked(invoice);
        if (linked) ReceiptInvoiceGuard.EnsureDraftFieldsLocked(invoice, input);
        var shipmentLinked = ShipmentInvoiceLinks.IsLinked(invoice);
        if (shipmentLinked) ShipmentInvoiceGuard.EnsureDraftFieldsLocked(invoice, input);
        var returnLinked = invoice.Kind == InvoiceKind.SalesReturn && invoice.SourceType == ReturnService.Source;
        if (returnLinked) ReturnService.EnsureDraftFieldsLocked(invoice, input);
        var built = await builder.BuildAsync(input, invoice.Id, false, ct);
        // Sevke bağlı olmayan satış faturalarında sevk satırı bağı tutulmaz.
        if (!shipmentLinked && invoice.Kind == InvoiceKind.Sales)
            foreach (var line in built.Lines) line.SourceLineId = null;

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            if (linked) {
                // Mal kabul kilidi altında faturalanan miktar (diğer taslaklar ve kesilmiş faturalar dahil) mal kabul miktarını aşamaz.
                var receiptIds = await receiptGuard.ReceiptIdsAsync(invoice, ct);
                await db.LockReceiptsAsync(receiptIds, ct);
                await receiptGuard.EnsureNotOverInvoicedAsync(receiptIds, invoice.Id,
                                                              built.Lines.Where(l => l.SourceLineId != null).Select(l => (l.SourceLineId!.Value, l.Quantity * l.Factor)), ct);
            }

            if (shipmentLinked && await shipmentGuard.OrderIdAsync(invoice, ct) is { } orderId) {
                // Sipariş kilidi altında faturalanan miktar (diğer taslaklar ve kesilmiş faturalar dahil) sevk miktarını aşamaz.
                await db.LockSalesOrderAsync(orderId, ct);
                await ShipmentInvoiceLinks.EnsureNotOverInvoicedAsync(db, invoice.Id,
                                                                      built.Lines.Where(l => l.SourceLineId != null).Select(l => (l.SourceLineId!.Value, l.Quantity * l.Factor)), ct);
            }

            if (returnLinked) await returns.EnsureDraftQuantitiesAsync(invoice, built.Lines, ct);

            // Önce eski satırları sil (satır sırası tekil indeksi), sonra yenilerini ve başlığı yaz.
            db.InvoiceLines.RemoveRange(invoice.Lines);
            db.InvoiceDueLines.RemoveRange(invoice.DueSchedule);
            invoice.Lines.Clear();
            invoice.DueSchedule.Clear();
            await db.SaveChangesAsync(ct);

            InvoiceBuilder.Apply(invoice, input, built);
            invoice.LastModified = clock.GetUtcNow(); // başlık satırı her zaman güncellenir: xmin değişir, eşzamanlı yazım yakalanır
            foreach (var line in built.Lines) {
                line.InvoiceId = invoice.Id;
                invoice.Lines.Add(line);
            }

            foreach (var due in built.Due) {
                due.InvoiceId = invoice.Id;
                invoice.DueSchedule.Add(due);
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw InvoiceErrors.VersionConflict();
        } catch (DbUpdateException ex) when (InvoicingErrors.IsUniqueViolation(ex)) {
            db.ClearTracking();
            throw InvoiceErrors.ExternalNumberTaken();
        }

        return InvoiceRules.ToDto(invoice, built.Cari.Name);
    }
}
