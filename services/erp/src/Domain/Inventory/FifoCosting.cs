namespace ERP.Domain.Inventory;

public static class FifoCosting {
    /// <summary>
    ///     Katmanları en eskiden başlayarak tüketir. Katmanlar biterse (eksi stok) açık kalan miktar
    ///     fallbackUnitCost ile maliyetlenir. Katmanların RemainingQty değeri değiştirilmez.
    /// </summary>
    public static (decimal TotalCost, IReadOnlyList<(Guid LayerId, decimal Qty)> Consumed) Consume(
        IReadOnlyList<CostLayer> layersOldestFirst, decimal outQty, decimal fallbackUnitCost) {
        var consumed = new List<(Guid LayerId, decimal Qty)>();
        var total    = 0m;
        var left     = outQty;

        foreach (var layer in layersOldestFirst) {
            if (left <= 0m) break;
            if (layer.RemainingQty <= 0m) continue;

            var take = Math.Min(layer.RemainingQty, left);
            consumed.Add((layer.Id, take));
            total += take * layer.UnitCost;
            left  -= take;
        }

        if (left > 0m) total += left * fallbackUnitCost;

        return (total, consumed);
    }
}
