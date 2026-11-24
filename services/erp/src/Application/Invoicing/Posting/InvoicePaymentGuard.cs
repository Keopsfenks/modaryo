namespace ERP.Application.Invoicing.Posting;

/// <summary>İptali engelleyen elle eşleştirme (tahsilat/ödeme) var mı? P5 gerçek denetimi sağlar; P4 hiçbir şeyi engellemez.</summary>
public interface IInvoicePaymentGuard {
    Task<bool> HasManualAllocationsAsync(Guid invoiceId, CancellationToken ct);
}

public sealed class NoInvoicePaymentGuard : IInvoicePaymentGuard {
    public Task<bool> HasManualAllocationsAsync(Guid invoiceId, CancellationToken ct) {
        return Task.FromResult(false);
    }
}
