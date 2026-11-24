namespace ERP.Application.Invoicing.Posting;

/// <summary>İptali engelleyen elle eşleştirme (tahsilat/ödeme) var mı? Gerçek denetim Treasury.InvoicePaymentGuard içindedir.</summary>
public interface IInvoicePaymentGuard {
    Task<bool> HasManualAllocationsAsync(Guid invoiceId, CancellationToken ct);
}
