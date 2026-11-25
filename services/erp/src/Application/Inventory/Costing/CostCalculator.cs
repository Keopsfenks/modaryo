using ERP.Application.Common.Interfaces;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Costing;

/// <summary>Yeniden oynatılan FIFO katmanı (bellekte).</summary>
internal sealed class ReplayLayer {
    public Guid     MovementId { get; init; }
    public DateOnly Date       { get; init; }
    public long     Sequence   { get; init; }
    public decimal  Original   { get; init; }
    public decimal  Remaining  { get; set; }
    public decimal  UnitCost   { get; set; }
}

/// <summary>TotalCost her zaman pozitif (mutlak) tutardır; yön hareketin miktarından gelir.</summary>
internal sealed record ReplayCost(decimal UnitCost, decimal TotalCost);

internal sealed class ReplayResult {
    public decimal                      Quantity    { get; set; }
    public decimal                      AverageCost { get; set; }
    public List<ReplayLayer>            Layers      { get; } = new();
    public Dictionary<Guid, ReplayCost> Costs       { get; } = new();
}

/// <summary>
///     Bir depo x kalemin hareketlerini (Date, Sequence) sırasıyla baştan oynatır; her hareketin maliyetini, anlık ortalamayı
///     ve FIFO katmanlarını hesaplar. Canlı yazımla (StockLedger) aynı yuvarlamayı kullanır. Saf bellek hesabıdır.
///     Sabit maliyetli satırlar: girişler (kendi birim maliyeti), transfer girişi (kaynak çıkıştan gelir; çıkışı yeniden
///     maliyetleyen iş onu günceller), girişin iptali olan çıkış (girişin maliyeti). Hesaplanan satırlar: normal çıkışlar
///     ve çıkışın iptali olan girişler (iptal edilen çıkışın yeni maliyeti).
/// </summary>
internal static class CostReplay {
    public const string ReversalPrefix   = "reversal:";
    public const string CorrectionPrefix = "correction:";
    public const string TransferPrefix   = "transfer-of:";

    public static decimal Round2(decimal v) {
        return Math.Round(v, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal Round6(decimal v) {
        return Math.Round(v, 6, MidpointRounding.AwayFromZero);
    }

    public static Guid? Referenced(StockMovement m, string prefix) {
        return m.Note is not null && m.Note.StartsWith(prefix, StringComparison.Ordinal)
               && Guid.TryParse(m.Note.AsSpan(prefix.Length), out var id)
                   ? id
                   : null;
    }

    /// <summary>frozen true dönen satırların kayıtlı maliyeti korunur (dönem kilidi ya da FromDate öncesi).</summary>
    public static ReplayResult Run(IReadOnlyList<StockMovement> ordered, CostingMethod method, Func<StockMovement, bool> frozen) {
        var result = new ReplayResult();
        var byId   = ordered.ToDictionary(m => m.Id);
        var layers = new Dictionary<Guid, ReplayLayer>();
        var qty    = 0m;
        var avg    = 0m;

        void RefreshAverage() {
            var remaining = result.Layers.Sum(l => l.Remaining);
            if (remaining > 0m) avg = Round6(result.Layers.Sum(l => l.Remaining * l.UnitCost) / remaining);
        }

        // Maliyet düzeltmeleri (Revaluation) kendi sıralarında değil, düzelttikleri girişin hemen ardından uygulanır: aynı gün sonradan
        // çıkan transfer / satış düzeltilmiş maliyeti alır. Girişi bu listede olmayan düzeltme kendi sırasında uygulanır.
        var deferred = ordered.Where(c => c.Quantity == 0m && Referenced(c, CorrectionPrefix) is { } t && byId.TryGetValue(t, out var tm) && tm.Quantity > 0m)
                              .GroupBy(c => Referenced(c, CorrectionPrefix)!.Value).ToDictionary(g => g.Key, g => g.ToList());

        void ApplyCorrection(StockMovement c) {
            var target = Referenced(c, CorrectionPrefix);
            var delta  = c.TotalCost;
            if (method == CostingMethod.WeightedAverage) {
                if (qty > 0m) avg = Round6((qty * avg + delta) / qty);
            } else if (target is { } id && layers.TryGetValue(id, out var corrected) && corrected.Original > 0m) {
                corrected.UnitCost = Round6(corrected.UnitCost + delta / corrected.Original);
                RefreshAverage();
            }

            result.Costs[c.Id] = new ReplayCost(c.UnitCost, c.TotalCost);
        }

        foreach (var m in ordered) {
            if (m.Quantity == 0m) {
                if (Referenced(m, CorrectionPrefix) is { } t && deferred.ContainsKey(t)) continue; // girişin konumunda uygulanır
                ApplyCorrection(m);
                continue;
            }

            var reversalOf = Referenced(m, ReversalPrefix);
            var original   = reversalOf is { } rid && byId.TryGetValue(rid, out var o) ? o : null;
            var isFrozen   = frozen(m);

            if (m.Quantity > 0m) {
                var unit = m.UnitCost;
                // Bir çıkışın iptali: çıkışın (yeni) maliyetiyle geri girer.
                if (original is { Quantity: < 0m } && !isFrozen && result.Costs.TryGetValue(original.Id, out var originalCost))
                    unit = originalCost.UnitCost;

                var total = isFrozen ? m.TotalCost : Round2(m.Quantity * unit);
                result.Costs[m.Id] = new ReplayCost(unit, total);

                var previous = qty;
                var (newQty, newAvg) = WeightedAverage.ApplyInbound(qty, avg, m.Quantity, unit);
                qty = newQty;
                avg = Round6(newAvg);

                if (method == CostingMethod.Fifo) {
                    // Eksi stoktan gelen açık, yeni girişin ilk kısmını kapatır.
                    var offset = Math.Min(m.Quantity, Math.Max(0m, -previous));
                    var layer = new ReplayLayer {
                        MovementId = m.Id, Date = m.Date, Sequence = m.Sequence, Original = m.Quantity,
                        Remaining  = m.Quantity - offset, UnitCost = unit
                    };
                    layers[m.Id] = layer;
                    result.Layers.Add(layer);
                }

                if (deferred.TryGetValue(m.Id, out var corrections))
                    foreach (var c in corrections) ApplyCorrection(c);

                continue;
            }

            // Çıkış.
            var     outQty = -m.Quantity;
            decimal outUnit;
            decimal outTotal;
            if (method == CostingMethod.Fifo) {
                var candidates = result.Layers.Where(l => l.Remaining > 0m).OrderBy(l => l.Date).ThenBy(l => l.Sequence).ToList();
                if (original is { Quantity: > 0m } && layers.TryGetValue(original.Id, out var preferred) && preferred.Remaining > 0m) {
                    candidates.Remove(preferred);
                    candidates.Insert(0, preferred);
                }

                (outTotal, outUnit) = ConsumeFifo(candidates, outQty, avg);
                RefreshAverage();
                qty -= outQty;
            } else {
                decimal? fixedUnit = original is { Quantity: > 0m } && result.Costs.TryGetValue(original.Id, out var oc) ? oc.UnitCost : null;
                outUnit  = fixedUnit ?? Round6(avg);
                outTotal = outUnit * outQty;
                var before = qty;
                qty -= outQty;
                // Bir girişin iptali değeri girişin maliyetiyle düşer; normal çıkış ortalamayı değiştirmez.
                if (fixedUnit is not null && qty > 0m) avg = Round6((before * avg - outQty * outUnit) / qty);
            }

            result.Costs[m.Id] = isFrozen
                                     ? new ReplayCost(m.UnitCost, -m.TotalCost)
                                     : new ReplayCost(outUnit, Round2(outTotal));
        }

        result.Quantity    = qty;
        result.AverageCost = avg;
        return result;
    }

    /// <summary>Katmanlardan tüketir (Remaining düşer); katman yetmezse açık fallback ile maliyetlenir.</summary>
    public static (decimal Total, decimal Unit) ConsumeFifo(IReadOnlyList<ReplayLayer> orderedLayers, decimal qty, decimal fallbackUnit) {
        var left  = qty;
        var total = 0m;
        foreach (var layer in orderedLayers) {
            if (left <= 0m) break;
            var take = Math.Min(layer.Remaining, left);
            layer.Remaining -= take;
            total           += take * layer.UnitCost;
            left            -= take;
        }

        if (left > 0m) total += left * fallbackUnit;
        return (total, qty == 0m ? 0m : Round6(total / qty));
    }
}

/// <summary>Bir çıkışın hareket sırasındaki birim maliyetini hesaplar (okuma amaçlı; hiçbir şey yazmaz).</summary>
public sealed class CostCalculator(
    IApplicationDbContext db,
    ITenantSettings       settings
) {
    /// <summary>
    ///     (date, sequence) konumundan ÖNCEKİ hareketlerin durumuna göre, qty kadar çıkışın birim maliyeti.
    ///     sequence = long.MaxValue o tarihin sonunu belirtir.
    /// </summary>
    public async Task<decimal> IssueUnitCostAsync(Guid warehouseId, Guid productId, Guid? variantId, DateOnly date, long sequence,
                                                  decimal qty, CancellationToken ct) {
        var method = (await settings.GetAsync<StockSettings>(ct)).Method;
        var movements = await db.StockMovements.AsNoTracking()
                                .Where(m => m.WarehouseId == warehouseId && m.ProductId == productId && m.VariantId == variantId
                                            && (m.Date < date || (m.Date == date && m.Sequence < sequence)))
                                .OrderBy(m => m.Date).ThenBy(m => m.Sequence).ToListAsync(ct);
        var state = CostReplay.Run(movements, method, _ => true);
        if (method == CostingMethod.WeightedAverage) return CostReplay.Round6(state.AverageCost);

        var layers = state.Layers.Where(l => l.Remaining > 0m).OrderBy(l => l.Date).ThenBy(l => l.Sequence).ToList();
        return CostReplay.ConsumeFifo(layers, qty, state.AverageCost).Unit;
    }
}
