namespace ERP.Domain.Purchasing;

public sealed record MatchRow(Guid PoLineId, decimal Ordered, decimal Received, decimal Invoiced, decimal PoPrice,
    decimal? ReceiptPrice, decimal? InvoicePrice, bool QtyMismatch, bool PriceVariance);

public static class ThreeWayMatch {
    /// <summary>
    ///     Miktar uyuşmazlığı: faturalanan miktar gelen miktardan farklıdır. Fiyat farkı: fatura birim fiyatı ile
    ///     mal kabul birim fiyatı (aynı para biriminde) arasındaki yüzde fark eşiği aşar.
    /// </summary>
    public static MatchRow Evaluate(Guid poLineId, decimal ordered, decimal received, decimal invoiced,
        decimal poPrice, decimal? receiptPrice, decimal? invoicePrice, decimal variancePercent) {
        var qtyMismatch = invoiced != received;
        var variance = receiptPrice is > 0m && invoicePrice is not null
            && Math.Abs(invoicePrice.Value - receiptPrice.Value) / receiptPrice.Value * 100m > variancePercent;
        return new MatchRow(poLineId, ordered, received, invoiced, poPrice, receiptPrice, invoicePrice, qtyMismatch, variance);
    }
}
