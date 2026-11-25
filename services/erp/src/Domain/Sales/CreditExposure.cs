namespace ERP.Domain.Sales;

public static class CreditExposure {
    /// <summary>Risk = cari bakiyesi + faturalanmamış onaylı siparişler + bu sipariş; limit aşılırsa onay gerekir (eşitlik aşım değildir).</summary>
    public static bool NeedsApproval(decimal? limit, decimal balance, decimal openOrders, decimal thisOrder) =>
        limit.HasValue && balance + openOrders + thisOrder > limit.Value;
}
