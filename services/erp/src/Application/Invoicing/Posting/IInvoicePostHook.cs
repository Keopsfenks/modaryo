using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Posting;

/// <summary>
///     Fatura kesim ve iptalinde modüllerin araya girdiği genişletme noktası. Çağıran işlem açık ve cari kilidi alınmıştır.
///     Stok anahtarları <see cref="StockKeysAsync" /> ile önceden verilir; <see cref="InvoicePoster" /> bunları kendi anahtarlarıyla
///     tek seferde kilitler, sonra <see cref="OnIssuedAsync" /> / <see cref="OnCancelledAsync" /> çağrılır.
/// </summary>
public interface IInvoicePostHook {
    Task<IReadOnlyList<(Guid WarehouseId, Guid ProductId, Guid? VariantId)>> StockKeysAsync(Invoice invoice, bool cancelling, CancellationToken ct);

    Task OnIssuedAsync(Invoice invoice, CancellationToken ct);

    Task OnCancelledAsync(Invoice invoice, CancellationToken ct);
}
