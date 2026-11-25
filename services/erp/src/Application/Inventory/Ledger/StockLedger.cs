using ERP.Application.Common.Interfaces;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Ledger;

/// <summary>Bir yazım çağrısının ayarları ve oturum bilgisi.</summary>
internal sealed record LedgerContext(CostingMethod Method, bool AllowNegative, string Actor);

internal sealed record MinWatch(Guid WarehouseId, Guid ProductId, Guid? VariantId, decimal MinQty, decimal Before);

/// <summary>
///     Tek bir hareketi yazan çekirdek: maliyetleme (ağırlıklı ortalama / FIFO), eksi stok denetimi, geriye dönük
///     hareket için yeniden değerleme işi ve minimum stok olayı. Çağıran önce depo x kalem kilidini almış olmalıdır.
/// </summary>
internal sealed class StockLedger(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ITenantSettings       settings,
    IOutbox               outbox
) {
    public async Task<LedgerContext> BeginAsync(CancellationToken ct) {
        var stock = await settings.GetAsync<StockSettings>(ct);
        return new LedgerContext(stock.Method, stock.AllowNegative, tenant.UserFullName ?? "Sistem");
    }

    public static decimal Round2(decimal v) {
        return Math.Round(v, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal Round6(decimal v) {
        return Math.Round(v, 6, MidpointRounding.AwayFromZero);
    }

    // ---- bakiyeler -------------------------------------------------------------------------------------------

    public async Task<decimal> PhysicalAsync(Guid warehouseId, Guid productId, Guid? variantId, CancellationToken ct) {
        return await db.StockMovements
                       .Where(m => m.WarehouseId == warehouseId && m.ProductId == productId && m.VariantId == variantId)
                       .SumAsync(m => (decimal?)m.Quantity, ct) ?? 0m;
    }

    /// <summary>Stoğu karşılanmış (bekleyen girişe bağlı olmayan) aktif rezervasyonların toplamı.</summary>
    public async Task<decimal> ReservedAsync(Guid warehouseId, Guid productId, Guid? variantId, CancellationToken ct) {
        return await db.Reservations
                       .Where(r => r.WarehouseId == warehouseId && r.ProductId == productId && r.VariantId == variantId
                                   && r.Status == ReservationStatus.Active && r.ExpectedReceiptSourceId == null)
                       .SumAsync(r => (decimal?)r.Quantity, ct) ?? 0m;
    }

    /// <summary>Kullanılabilir = fiziksel - rezerve; yalnızca Normal depoda. Diğer depolarda sıfır.</summary>
    public async Task<decimal> AvailableAsync(Warehouse warehouse, Guid productId, Guid? variantId, CancellationToken ct) {
        if (warehouse.Kind != WarehouseKind.Normal) return 0m;

        return await PhysicalAsync(warehouse.Id, productId, variantId, ct)
               - await ReservedAsync(warehouse.Id, productId, variantId, ct);
    }

    // ---- minimum stok ----------------------------------------------------------------------------------------

    public async Task<MinWatch?> WatchMinAsync(Warehouse warehouse, Guid productId, Guid? variantId, CancellationToken ct) {
        if (warehouse.Kind != WarehouseKind.Normal) return null;

        var min = await db.MinStockLevels.AsNoTracking()
                          .FirstOrDefaultAsync(m => m.WarehouseId == warehouse.Id && m.ProductId == productId
                                                    && m.VariantId == variantId, ct);
        if (min is null) return null;

        return new MinWatch(warehouse.Id, productId, variantId, min.MinQty,
                            await AvailableAsync(warehouse, productId, variantId, ct));
    }

    /// <summary>Kullanılabilir miktar minimumun üstünden altına geçtiyse tek bir olay yazar.</summary>
    public async Task CheckMinAsync(MinWatch? watch, Warehouse warehouse, CancellationToken ct) {
        if (watch is null) return;

        var after = await AvailableAsync(warehouse, watch.ProductId, watch.VariantId, ct);
        if (watch.Before >= watch.MinQty && after < watch.MinQty)
            outbox.Enqueue("stock.below-minimum",
                           new BelowMinimumData(watch.WarehouseId, watch.ProductId, watch.VariantId, after, watch.MinQty));
    }

    // ---- yeniden değerleme işi -------------------------------------------------------------------------------

    /// <summary>Aynı depo x kalem için tek iş tutulur; tarih en erkene birleşir.</summary>
    public async Task EnqueueRevaluationAsync(Guid warehouseId, Guid productId, Guid? variantId, DateOnly fromDate, CancellationToken ct) {
        var job = await db.RevaluationJobs.FirstOrDefaultAsync(j => j.WarehouseId == warehouseId && j.ProductId == productId
                                                                    && j.VariantId == variantId, ct);
        if (job is null) {
            db.RevaluationJobs.Add(new RevaluationJob {
                WarehouseId = warehouseId, ProductId = productId, VariantId = variantId, FromDate = fromDate,
                EnqueuedAt  = DateTimeOffset.UtcNow
            });
        } else if (fromDate < job.FromDate) {
            job.FromDate = fromDate;
        }
    }

    // ---- yazım -----------------------------------------------------------------------------------------------

    private async Task<AverageCostSnapshot> SnapshotAsync(Guid warehouseId, Guid productId, Guid? variantId, CancellationToken ct) {
        var snapshot = await db.AverageCosts.FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductId == productId
                                                                      && s.VariantId == variantId, ct);
        if (snapshot is not null) return snapshot;

        snapshot = new AverageCostSnapshot { WarehouseId = warehouseId, ProductId = productId, VariantId = variantId };
        db.AverageCosts.Add(snapshot);
        return snapshot;
    }

    private static StockMovement NewMovement(LedgerContext ctx, Guid warehouseId, Guid productId, Guid? variantId, MovementType type,
                                             DateOnly date, string sourceType, Guid sourceId, Guid? lineId, string? note) {
        return new StockMovement {
            WarehouseId = warehouseId, ProductId = productId, VariantId = variantId, Type = type, Date = date,
            SourceType  = sourceType, SourceId = sourceId, SourceLineId = lineId, Note = note,
            CreatedAt   = DateTimeOffset.UtcNow, ActorLabel = ctx.Actor
        };
    }

    public async Task<StockMovement> InboundAsync(LedgerContext ctx, Warehouse warehouse, Guid productId, Guid? variantId,
                                                  MovementType type, DateOnly date, decimal quantity, decimal? unitCost,
                                                  string sourceType, Guid sourceId, Guid? lineId, string? note,
                                                  CancellationToken ct) {
        var profile  = await db.StockProfileAsync(warehouse.Id, productId, variantId, date, ct);
        var snapshot = await SnapshotAsync(warehouse.Id, productId, variantId, ct);
        var unit     = Round6(unitCost ?? snapshot.AverageCost);
        var previous = snapshot.Quantity;

        var movement = NewMovement(ctx, warehouse.Id, productId, variantId, type, date, sourceType, sourceId, lineId, note);
        movement.Quantity  = quantity;
        movement.UnitCost  = unit;
        movement.TotalCost = Round2(quantity * unit);
        db.StockMovements.Add(movement);

        var (newQty, newAvg) = WeightedAverage.ApplyInbound(snapshot.Quantity, snapshot.AverageCost, quantity, unit);
        snapshot.Quantity    = newQty;
        snapshot.AverageCost = Round6(newAvg);

        await db.SaveChangesAsync(ct); // Sequence atanır

        if (ctx.Method == CostingMethod.Fifo) {
            // Eksi stoktan gelen açık, yeni girişin ilk kısmını kapatır.
            var offset = Math.Min(quantity, Math.Max(0m, -previous));
            db.CostLayers.Add(new CostLayer {
                WarehouseId = warehouse.Id, ProductId = productId, VariantId = variantId, InboundMovementId = movement.Id,
                Date        = date, Sequence = movement.Sequence, OriginalQty = quantity, RemainingQty = quantity - offset,
                UnitCost    = unit
            });
        }

        if (profile.LastDate is { } last && date < last)
            await EnqueueRevaluationAsync(warehouse.Id, productId, variantId, date, ct);

        await db.SaveChangesAsync(ct);
        return movement;
    }

    /// <summary>
    ///     Çıkış hareketi. reversalUnitCost / preferLayerMovementId yalnızca bir girişin iptalinde verilir:
    ///     çıkış, girişin kendi maliyetiyle (FIFO'da kendi katmanından) yapılır.
    /// </summary>
    public async Task<StockMovement> OutboundAsync(LedgerContext ctx, Warehouse warehouse, Guid productId, Guid? variantId,
                                                   MovementType type, DateOnly date, decimal quantity, string sourceType,
                                                   Guid sourceId, Guid? lineId, string? note, bool exempt, string itemLabel,
                                                   decimal? reversalUnitCost, Guid? preferLayerMovementId, CancellationToken ct) {
        var profile = await db.StockProfileAsync(warehouse.Id, productId, variantId, date, ct);

        if (!ctx.AllowNegative && !exempt) {
            // Geriye dönük kontrol: bu tarihte ve sonrasındaki hiçbir kümülatif bakiye eksiye düşmemeli.
            if (profile.AtDate - quantity < 0m || (profile.MinAfter is { } later && later - quantity < 0m))
                throw InventoryErrors.InsufficientStock(itemLabel);

            // Rezervasyonlar satılmamalı: çıkıştan sonra kalan, stoğu karşılanmış rezervasyonları karşılamalı.
            if (warehouse.Kind == WarehouseKind.Normal && type != MovementType.CountAdjustment) {
                var reserved = await ReservedAsync(warehouse.Id, productId, variantId, ct);
                if (profile.Total - quantity < reserved) throw InventoryErrors.InsufficientStock(itemLabel);
            }
        }

        var     snapshot = await SnapshotAsync(warehouse.Id, productId, variantId, ct);
        decimal unit;
        decimal total;
        if (ctx.Method == CostingMethod.Fifo) {
            var layers = await db.CostLayers
                                 .Where(l => l.WarehouseId == warehouse.Id && l.ProductId == productId && l.VariantId == variantId
                                             && l.RemainingQty > 0m)
                                 .OrderBy(l => l.Date).ThenBy(l => l.Sequence).ToListAsync(ct);
            if (preferLayerMovementId is { } preferred) {
                var first = layers.FirstOrDefault(l => l.InboundMovementId == preferred);
                if (first is not null) {
                    layers.Remove(first);
                    layers.Insert(0, first);
                }
            }

            var (cost, consumed) = FifoCosting.Consume(layers, quantity, snapshot.AverageCost);
            foreach (var (layerId, qty) in consumed) layers.First(l => l.Id == layerId).RemainingQty -= qty;

            total = cost;
            unit  = quantity == 0m ? 0m : Round6(cost / quantity);
            var remaining = layers.Sum(l => l.RemainingQty);
            if (remaining > 0m) snapshot.AverageCost = Round6(layers.Sum(l => l.RemainingQty * l.UnitCost) / remaining);
            snapshot.Quantity -= quantity;
        } else {
            unit  = Round6(reversalUnitCost ?? snapshot.AverageCost);
            total = unit * quantity;
            var before = snapshot.Quantity;
            snapshot.Quantity -= quantity;
            // Bir girişin iptali değeri girişin maliyetiyle düşer; normal çıkış ortalamayı değiştirmez.
            if (reversalUnitCost is not null && snapshot.Quantity > 0m)
                snapshot.AverageCost = Round6((before * snapshot.AverageCost - quantity * unit) / snapshot.Quantity);
        }

        var movement = NewMovement(ctx, warehouse.Id, productId, variantId, type, date, sourceType, sourceId, lineId, note);
        movement.Quantity  = -quantity;
        movement.UnitCost  = unit;
        movement.TotalCost = -Round2(total);
        db.StockMovements.Add(movement);

        if (profile.LastDate is { } last && date < last)
            await EnqueueRevaluationAsync(warehouse.Id, productId, variantId, date, ct);

        await db.SaveChangesAsync(ct);
        return movement;
    }

    /// <summary>Maliyet düzeltmesi: Revaluation hareketi (miktar 0, toplam = delta) ve ortalama / katman güncellemesi.</summary>
    public async Task<StockMovement> CorrectCostAsync(LedgerContext ctx, Warehouse warehouse, StockMovement inbound, DateOnly date,
                                                      decimal delta, string sourceType, Guid sourceId, CancellationToken ct) {
        var productId = inbound.ProductId;
        var variantId = inbound.VariantId;

        if (ctx.Method == CostingMethod.WeightedAverage) {
            // Düzeltme, girişin konumunda uygulanır (yeniden maliyetleme işi sonradan çıkan transfer / satışları da düzeltir); bu yüzden
            // düzeltme günü sonunda stok kalmamış olması engel değildir.
            var snapshot = await SnapshotAsync(warehouse.Id, productId, variantId, ct);
            if (snapshot.Quantity > 0m)
                snapshot.AverageCost = Round6((snapshot.Quantity * snapshot.AverageCost + delta) / snapshot.Quantity);
        } else {
            var layer = await db.CostLayers.FirstOrDefaultAsync(l => l.InboundMovementId == inbound.Id, ct);
            if (layer is not null && layer.OriginalQty > 0m) {
                layer.UnitCost = Round6(layer.UnitCost + delta / layer.OriginalQty);
                var layers = await db.CostLayers
                                     .Where(l => l.WarehouseId == warehouse.Id && l.ProductId == productId && l.VariantId == variantId
                                                 && l.RemainingQty > 0m).ToListAsync(ct);
                var remaining = layers.Sum(l => l.RemainingQty);
                if (remaining > 0m) {
                    var snapshot = await SnapshotAsync(warehouse.Id, productId, variantId, ct);
                    snapshot.AverageCost = Round6(layers.Sum(l => l.RemainingQty * l.UnitCost) / remaining);
                }
            }
        }

        var movement = NewMovement(ctx, warehouse.Id, productId, variantId, MovementType.Revaluation, date, sourceType, sourceId,
                                   null, $"correction:{inbound.Id}");
        movement.Quantity  = 0m;
        movement.UnitCost  = 0m;
        movement.TotalCost = Round2(delta);
        db.StockMovements.Add(movement);

        // Yeniden maliyetleme, düzeltilen girişin tarihinden başlar: tekrar oynatma düzeltmeyi girişin konumuna taşır, giriş ile
        // düzeltme tarihi arasındaki çıkışlar da yeniden maliyetlenmelidir (aksi halde donmuş kalıp payları kaybolur).
        await EnqueueRevaluationAsync(warehouse.Id, productId, variantId, date < inbound.Date ? date : inbound.Date, ct);
        await db.SaveChangesAsync(ct);
        return movement;
    }
}
