using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing.Posting;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Commands;

[Authorize(Permission = InvoicingPermissions.Cancel)]
public sealed record CancelInvoiceCommand(Guid Id, string Reason) : IRequest;

public sealed class CancelInvoiceCommandValidator : AbstractValidator<CancelInvoiceCommand> {
    public CancelInvoiceCommandValidator() {
        RuleFor(c => c.Reason).Must(r => r is not null && r.Trim().Length is >= 2 and <= 200)
            .WithMessage("İptal nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

/// <summary>
///     Kesilmiş faturayı iptal eder: defter hareketi, vade satırları (hareketle birlikte devre dışı kalır), eşleştirmeler ve stok
///     tek işlemde geri alınır; numara kullanılmış kalır. Kesimle aynı kilit sırası: önce cari, sonra stok.
/// </summary>
public sealed class CancelInvoiceCommandHandler(
    IApplicationDbContext   db,
    ICurrentTenant          tenant,
    ICurrentSession         session,
    IPeriodLock             periodLock,
    InvoicePoster           poster,
    IInvoicePaymentGuard    paymentGuard,
    TimeProvider            clock
) : IRequestHandler<CancelInvoiceCommand> {
    public async Task Handle(CancelInvoiceCommand request, CancellationToken ct) {
        var reason = request.Reason.Trim();

        // Kilit için yalnızca cari ve tür okunur (izlemesiz); durum kilit alındıktan sonra yeniden okunur.
        var head = await db.Invoices.AsNoTracking().Where(i => i.Id == request.Id).Select(i => new { i.Kind, i.BranchId, i.CurrentAccountId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "Invoice");
        InvoiceRules.EnsureKindPermission(session, head.Kind);
        BranchGuard.Ensure(tenant, head.BranchId);

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            await db.LockCurrentAccountAsync(head.CurrentAccountId, ct);

            var invoice = await db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == request.Id, ct)
                          ?? throw new NotFoundException(request.Id.ToString(), "Invoice");
            if (invoice.Status == InvoiceStatus.Cancelled) throw InvoiceErrors.Conflict("invoice_cancelled", "Fatura zaten iptal edilmiş.");
            if (invoice.Status != InvoiceStatus.Issued)
                throw InvoiceErrors.Conflict("invoice_not_issued", "Yalnızca kesilmiş faturalar iptal edilebilir; taslak faturalar silinir.");

            // İadeler asıl faturayla aynı cari kilidini alır: iptal ile iade kesimi birbirini aynı kilit altında görür. Yalnızca kesilmiş iadeler
            // defter/stok etkisi taşır (taslaklar kesim anında original_cancelled ile reddedilir; iptal edilmiş iadeler sayılmaz).
            if (!InvoiceRules.IsReturn(invoice.Kind)
                && await db.Invoices.AnyAsync(i => i.ReturnOfInvoiceId == invoice.Id && i.Status == InvoiceStatus.Issued, ct))
                throw InvoiceErrors.Conflict("invoice_has_returns", "Faturaya bağlı iade faturaları var; önce iadeleri iptal edin ya da silin.");

            if (await paymentGuard.HasManualAllocationsAsync(invoice.Id, ct))
                throw InvoiceErrors.Conflict("invoice_has_payments", "Faturaya bağlı tahsilat/ödeme eşleştirmesi var; önce eşleştirmeleri kaldırın.");

            await periodLock.EnsureOpenAsync(invoice.Date, ct);

            invoice.Status       = InvoiceStatus.Cancelled;
            invoice.CancelReason = reason;
            invoice.CancelledAt  = clock.GetUtcNow();
            invoice.LastModified = clock.GetUtcNow();
            await poster.ReverseAsync(invoice, reason, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw InvoiceErrors.Conflict("invoice_cancelled", "Fatura başkası tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin.");
        }
    }
}
