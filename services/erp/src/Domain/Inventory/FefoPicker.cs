namespace ERP.Domain.Inventory;

public static class FefoPicker {
    /// <summary>Son kullanma tarihi en yakın lottan başlar (tarihsiz en sonda), eşitlikte lot oluşturma sırası.</summary>
    public static IReadOnlyList<(Guid LotId, decimal Qty)> Suggest(
        IReadOnlyList<(Guid LotId, DateOnly? Expiry, DateTimeOffset Created, decimal Available)> lots, decimal qty) {
        var result = new List<(Guid LotId, decimal Qty)>();
        var left   = qty;

        var ordered = lots
            .Where(l => l.Available > 0m)
            .OrderBy(l => l.Expiry.HasValue ? 0 : 1)
            .ThenBy(l => l.Expiry)
            .ThenBy(l => l.Created);

        foreach (var lot in ordered) {
            if (left <= 0m) break;
            var take = Math.Min(lot.Available, left);
            result.Add((lot.LotId, take));
            left -= take;
        }

        return result;
    }
}
