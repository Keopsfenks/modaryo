namespace ERP.Domain.Purchasing;

public sealed record LandedTarget(Guid ReceiptLineId, decimal TlValue, decimal BaseQty, decimal? WeightKgPerUnit);

/// <summary>Ağırlık anahtarında ağırlığı olmayan hedef satırlar için fırlatılır.</summary>
public sealed class WeightMissingException(IReadOnlyList<Guid> receiptLineIds)
    : Exception("Ağırlık anahtarı için tüm kalemlerin ağırlığı tanımlı olmalıdır.") {
    public IReadOnlyList<Guid> ReceiptLineIds { get; } = receiptLineIds;
}

public static class LandedCostAllocator {
    /// <summary>En büyük kalan yöntemi: pay kuruşa indirilir, artan kuruşlar en büyük kalana (eşitlikte sıraya) verilir.</summary>
    public static IReadOnlyDictionary<Guid, decimal> Allocate(decimal totalTl, AllocationKey key,
        IReadOnlyList<LandedTarget> targets) {
        if (targets.Count == 0) throw new ArgumentException("En az bir hedef gerekir.", nameof(targets));
        if (totalTl < 0) throw new ArgumentException("Dağıtılacak tutar negatif olamaz.", nameof(totalTl));

        if (key == AllocationKey.Weight) {
            var missing = targets.Where(t => t.WeightKgPerUnit is null or <= 0m).Select(t => t.ReceiptLineId).ToList();
            if (missing.Count > 0) throw new WeightMissingException(missing);
        }

        var weights = targets.Select(t => key switch {
            AllocationKey.Amount   => t.TlValue,
            AllocationKey.Quantity => t.BaseQty,
            _                      => t.BaseQty * t.WeightKgPerUnit!.Value
        }).ToList();
        var sum = weights.Sum();
        if (sum <= 0m) throw new ArgumentException("Dağıtım anahtarı toplamı sıfır olamaz.");

        var totalCents = (long)Math.Round(totalTl * 100m, 0, MidpointRounding.AwayFromZero);
        var floors     = new long[targets.Count];
        var remainders = new decimal[targets.Count];
        long used = 0;
        for (var i = 0; i < targets.Count; i++) {
            var exact = totalCents * weights[i] / sum;
            floors[i]     = (long)Math.Floor(exact);
            remainders[i] = exact - floors[i];
            used         += floors[i];
        }

        var left = totalCents - used;
        foreach (var i in Enumerable.Range(0, targets.Count).OrderByDescending(i => remainders[i]).ThenBy(i => i).Take((int)left))
            floors[i]++;

        var result = new Dictionary<Guid, decimal>();
        for (var i = 0; i < targets.Count; i++)
            result[targets[i].ReceiptLineId] = floors[i] / 100m;
        return result;
    }
}
