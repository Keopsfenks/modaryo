namespace ERP.Domain.Inventory;

public static class WeightedAverage {
    /// <summary>Girişten sonraki (miktar, ortalama). Giriş öncesi miktar ≤ 0 ise ortalama giriş maliyetidir.</summary>
    public static (decimal NewQty, decimal NewAvg) ApplyInbound(decimal qty, decimal avg, decimal inQty, decimal inUnitCost) {
        var newQty = qty + inQty;
        if (qty <= 0m || newQty <= 0m) return (newQty, inUnitCost);

        var newAvg = (qty * avg + inQty * inUnitCost) / newQty;
        return (newQty, newAvg);
    }

    public static decimal IssueCost(decimal avg, decimal outQty) {
        return avg * outQty;
    }
}
