using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory.Documents;
using ERP.Application.Inventory.Tracking;
using ERP.Application.Invoicing;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;
using ERP.Domain.Shipping;

namespace ERP.Application.Shipping.Picking;

/// <summary>
///     Toplama listesi yazımı. Çağıran açık bir işlemde çağırır ve onaylar; servis ilgili siparişlerin kilidini (sıralı) kendisi alır.
///     Stok yazılmaz: toplama yalnızca sevk satırı miktarlarını, lot ve seri bilgisini belirler; stok çıkışı sevk anındadır.
///     Kilit sırası: cari, satış siparişi (kimliğe göre sıralı), ... stok anahtarları (burada stok kilidi gerekmez).
/// </summary>
internal sealed class PickListService(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ITenantSettings       settings,
    TrackingValidator     tracking,
    TimeProvider          clock
) {
    private const int MaxBatch = 100;

    private sealed record LotPool(Guid LotId, string LotNo, DateOnly? Expiry, DateTimeOffset Created, decimal Available);

    private async Task LockOrdersAsync(IEnumerable<Guid> orderIds, CancellationToken ct) {
        foreach (var id in orderIds.Distinct().Order())
            if (!await db.LockSalesOrderAsync(id, ct))
                throw new NotFoundException(id.ToString(), "SalesOrder");
        db.ClearTracking();
    }

    private static string Name(ShipmentStatus status) {
        return status.ToString();
    }

    public async Task<PickList> CreateAsync(IReadOnlyList<Guid> shipmentIds, CancellationToken ct) {
        var ids = shipmentIds.Distinct().ToList();
        if (ids.Count == 0) throw ShippingErrors.Bad("shipments_required", "Toplama listesi için en az bir sevk seçilmelidir.");
        if (ids.Count > MaxBatch) throw ShippingErrors.Bad("too_many_shipments", $"Tek toplama listesine en fazla {MaxBatch} sevk eklenebilir.");

        var heads = await db.Shipments.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.SalesOrderId, s.BranchId }).ToListAsync(ct);
        var missing = ids.FirstOrDefault(i => heads.All(h => h.Id != i));
        if (missing != Guid.Empty) throw new NotFoundException(missing.ToString(), "Shipment");
        foreach (var h in heads) BranchGuard.Ensure(tenant, h.BranchId);
        await LockOrdersAsync(heads.Select(h => h.SalesOrderId), ct);

        var shipments = await db.Shipments.Include(s => s.Lines).Where(s => ids.Contains(s.Id)).OrderBy(s => s.Number).ToListAsync(ct);
        var shipping  = await settings.GetAsync<ShippingSettings>(ct);
        foreach (var s in shipments)
            if (!ShipmentStatusMachine.CanMove(Name(s.Status), "Picking", shipping.UsePickPack))
                throw ShippingErrors.InvalidTransition(Name(s.Status), "Picking");
        if (shipments.Select(s => s.WarehouseId).Distinct().Count() > 1)
            throw ShippingErrors.Bad("pick_warehouse_mismatch", "Birleşik toplama listesindeki sevkler aynı depodan çıkmalıdır.");

        var warehouse = await db.Warehouses.AsNoTracking().FirstAsync(w => w.Id == shipments[0].WarehouseId, ct);
        var productIds = shipments.SelectMany(s => s.Lines).Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var list = new PickList {
            Number = await DocumentNumbers.NextAsync(db, "TL", DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3)), ct),
            BranchId = shipments[0].BranchId, WarehouseId = warehouse.Id, ShipmentIds = shipments.Select(s => s.Id).ToList()
        };
        var location = tenant.HasModule("wms") ? null : warehouse.Name;

        var groups = shipments.SelectMany(s => s.Lines.OrderBy(l => l.Sort)).Where(l => products[l.ProductId].Kind != ProductKind.Service)
                              .GroupBy(l => (l.ProductId, l.VariantId, l.Unit, l.Factor)).ToList();
        var pools  = new Dictionary<(Guid, Guid?), List<LotPool>>();
        var sort   = 1;
        foreach (var g in groups) {
            var product  = products[g.Key.ProductId];
            var quantity = g.Sum(l => l.Quantity);
            IReadOnlyList<PickLot>? suggested = null;
            if (TrackingValidator.IsTracked(product) && !product.IsBundle && TrackingValidator.ModeOf(product) == TrackingMode.Lot)
                suggested = await SuggestAsync(pools, product, g.Key.VariantId, warehouse.Id, Base(quantity, g.Key.Factor), ct);
            list.Lines.Add(new PickLine {
                PickListId = list.Id, Sort = sort++, WarehouseId = warehouse.Id, ProductId = product.Id, VariantId = g.Key.VariantId, Description = g.First().Description,
                Unit = g.Key.Unit, Factor = g.Key.Factor, Quantity = quantity, Location = location, SuggestedLots = suggested is { Count: > 0 } ? suggested : null,
                Sources = g.Select(l => new PickSource(l.Id, l.Quantity)).ToList()
            });
        }

        if (list.Lines.Count == 0) throw ShippingErrors.Conflict("nothing_to_pick", "Toplanacak fiziksel kalem yok (yalnızca hizmet satırları).");

        db.PickLists.Add(list);
        foreach (var s in shipments) {
            s.Status     = ShipmentStatus.Picking;
            s.PickListId = list.Id;
        }

        await db.SaveChangesAsync(ct);
        return list;
    }

    private static decimal Base(decimal quantity, decimal factor) {
        return Math.Round(quantity * factor, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>FEFO önerisi; aynı kalemin başka satırlarına önerilen miktar bakiyeden düşülür (aynı lotu iki kez önermez).</summary>
    private async Task<List<PickLot>> SuggestAsync(Dictionary<(Guid, Guid?), List<LotPool>> pools, Product product, Guid? variantId, Guid warehouseId, decimal baseQuantity,
                                                   CancellationToken ct) {
        if (!pools.TryGetValue((product.Id, variantId), out var pool)) {
            var balances = await tracking.LotBalancesAsync(product.Id, variantId, warehouseId, ct);
            var lotIds   = balances.Where(b => b.Value > 0m).Select(b => b.Key.LotId).ToList();
            var lots     = await db.Lots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToListAsync(ct);
            pool = lots.Select(l => new LotPool(l.Id, l.LotNo, l.ExpiryDate, l.CreatedAt, balances[(warehouseId, l.Id)])).ToList();
            pools[(product.Id, variantId)] = pool;
        }

        var picks = FefoPicker.Suggest(pool.Select(p => (p.LotId, p.Expiry, p.Created, p.Available)).ToList(), baseQuantity);
        var result = new List<PickLot>();
        foreach (var pick in picks) {
            var lot = pool.First(p => p.LotId == pick.LotId);
            pool[pool.IndexOf(lot)] = lot with { Available = lot.Available - pick.Qty };
            result.Add(new PickLot(lot.LotNo, lot.Expiry, pick.Qty, lot.LotId));
        }

        return result;
    }

    private async Task<(PickList List, Dictionary<Guid, Guid> OrderByShipment)> LoadForUpdateAsync(Guid id, CancellationToken ct) {
        var head = await db.PickLists.AsNoTracking().Where(p => p.Id == id).Select(p => new { p.BranchId, p.ShipmentIds }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(id.ToString(), "PickList");
        BranchGuard.Ensure(tenant, head.BranchId);
        var ids    = head.ShipmentIds.ToList();
        var orders = await db.Shipments.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.SalesOrderId }).ToListAsync(ct);
        await LockOrdersAsync(orders.Select(o => o.SalesOrderId), ct);
        var list = await db.PickLists.Include(p => p.Lines).FirstAsync(p => p.Id == id, ct);
        return (list, orders.ToDictionary(o => o.Id, o => o.SalesOrderId));
    }

    public async Task<PickList> UpdateLineAsync(Guid listId, Guid lineId, PickLineInput input, CancellationToken ct) {
        var (list, _) = await LoadForUpdateAsync(listId, ct);
        if (list.Status != PickListStatus.Open) throw ShippingErrors.Conflict("pick_list_closed", "Toplama listesi kapanmış; satırlar değiştirilemez.");
        var line = list.Lines.FirstOrDefault(l => l.Id == lineId) ?? throw new NotFoundException(lineId.ToString(), "PickLine");

        var qty = input.PickedQty;
        if (qty < 0m) throw ShippingErrors.Bad("picked_negative", "Toplanan miktar negatif olamaz.");
        if (Math.Round(qty, 6) != qty) throw ShippingErrors.Bad("quantity_scale", "Toplanan miktar en fazla 6 ondalık basamak içerebilir.");
        if (qty > line.Quantity) throw ShippingErrors.Bad("picked_exceeds_quantity", $"\"{line.Description}\" için toplanabilecek en fazla miktar {line.Quantity:0.######}.");

        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == line.ProductId, ct);
        List<PickLot>? lots    = null;
        List<string>?  serials = null;
        if (qty > 0m && TrackingValidator.IsTracked(product) && !product.IsBundle) {
            if (TrackingValidator.ModeOf(product) == TrackingMode.Lot) {
                var need = Base(qty, line.Factor);
                if (input.Lots is { Count: > 0 } given) {
                    if (given.Any(l => string.IsNullOrWhiteSpace(l.LotNo) || l.Quantity <= 0m))
                        throw ShippingErrors.Bad("lot_invalid", $"\"{line.Description}\": lot numarası ve miktarı zorunludur.");
                    if (Math.Round(given.Sum(l => l.Quantity), 6) != need)
                        throw ShippingErrors.Bad("lot_quantity_mismatch", $"\"{line.Description}\": lot miktarları toplamı {need:0.######} olmalıdır.");
                    lots = given.Select(l => new PickLot(l.LotNo.Trim(), l.Expiry, l.Quantity, l.LotId)).ToList();
                } else if (line.SuggestedLots is { Count: > 0 } suggested) {
                    lots = TakeLots(suggested, need);
                }
            } else {
                var list2 = InvoiceRules.NormalizeSerials(input.Serials);
                if (qty != Math.Truncate(qty) || list2 is null || list2.Count != (int)qty)
                    throw ShippingErrors.Bad("serial_quantity_mismatch", $"\"{line.Description}\": seri numarası sayısı toplanan miktara eşit olmalıdır.");
                if (list2.Distinct(StringComparer.Ordinal).Count() != list2.Count)
                    throw ShippingErrors.Bad("serial_duplicate", $"\"{line.Description}\": aynı seri numarası birden fazla kez girilemez.");
                serials = list2.ToList();
            }
        }

        line.PickedQuantity = qty;
        line.PickedLots     = lots;
        line.PickedSerials  = serials;
        await db.SaveChangesAsync(ct);
        return list;
    }

    private static List<PickLot> TakeLots(IReadOnlyList<PickLot> pool, decimal need) {
        var result = new List<PickLot>();
        foreach (var lot in pool) {
            if (need <= 0m) break;
            var take = Math.Min(lot.Quantity, need);
            result.Add(lot with { Quantity = take });
            need -= take;
        }

        return result;
    }

    /// <summary>
    ///     Toplamayı bitirir: eksik toplama (kısa toplama) sevk satırı miktarını düşürür; fark siparişin açık miktarında kalır
    ///     (taslak sevk açık miktarı yalnızca ayırdığından rezervasyona dokunulmaz). force=false iken işlenmemiş satır 409 pick_incomplete.
    /// </summary>
    public async Task<PickList> CompleteAsync(Guid listId, bool force, CancellationToken ct) {
        var (list, _) = await LoadForUpdateAsync(listId, ct);
        if (list.Status != PickListStatus.Open) throw ShippingErrors.Conflict("pick_list_closed", "Toplama listesi zaten kapanmış.");
        if (!force && list.Lines.Any(l => l.PickedQuantity is null)) throw ShippingErrors.PickIncomplete();

        var ids       = list.ShipmentIds.ToList();
        // Yalnızca bu listeye bağlı, hâlâ Picking olan sevkler işlenir (liste açıkken iptal/geri alınan sevke dokunulmaz).
        var shipments = await db.Shipments.Include(s => s.Lines).Where(s => ids.Contains(s.Id) && s.Status == ShipmentStatus.Picking && s.PickListId == listId).ToListAsync(ct);
        var byLine    = shipments.SelectMany(s => s.Lines).ToDictionary(l => l.Id);

        foreach (var line in list.Lines) {
            var remaining = line.PickedQuantity ?? 0m;
            line.PickedQuantity ??= 0m;
            var lotPool    = line.PickedLots?.Select(l => l with { }).ToList();
            var serialPool = line.PickedSerials?.ToList();
            foreach (var source in line.Sources ?? []) {
                if (!byLine.TryGetValue(source.ShipmentLineId, out var sl)) continue;
                var take = Math.Min(remaining, sl.Quantity);
                remaining  -= take;
                sl.Quantity = take;
                if (lotPool is not null) sl.Lots = PullLots(lotPool, Base(take, sl.Factor));
                if (serialPool is not null) {
                    var count = (int)Math.Truncate(take);
                    sl.Serials = count == 0 ? null : serialPool.Take(count).ToList();
                    serialPool.RemoveRange(0, Math.Min(count, serialPool.Count));
                }

                if (take == 0m) {
                    db.ShipmentLines.Remove(sl);
                    shipments.First(s => s.Id == sl.ShipmentId).Lines.Remove(sl);
                }
            }
        }

        if (shipments.Any(s => s.Lines.Count == 0))
            throw ShippingErrors.Conflict("pick_empty", "Bir sevkin hiçbir kalemi toplanmadı; önce sevki iptal edin ya da en az bir kalem toplayın.");

        list.Status = PickListStatus.Completed;
        await db.SaveChangesAsync(ct);
        return list;
    }

    private static List<InvoiceLot>? PullLots(List<PickLot> pool, decimal need) {
        var result = new List<InvoiceLot>();
        for (var i = 0; i < pool.Count && need > 0m; i++) {
            var take = Math.Min(pool[i].Quantity, need);
            if (take <= 0m) continue;
            result.Add(new InvoiceLot(pool[i].LotNo, pool[i].Expiry, take, pool[i].LotId));
            pool[i] =  pool[i] with { Quantity = pool[i].Quantity - take };
            need   -= take;
        }

        return result.Count == 0 ? null : result;
    }

    /// <summary>Açık toplama listesini iptal eder; sevkler taslağa döner (Picking -> Draft).</summary>
    public async Task<PickList> CancelAsync(Guid listId, CancellationToken ct) {
        var (list, _) = await LoadForUpdateAsync(listId, ct);
        if (list.Status != PickListStatus.Open) throw ShippingErrors.Conflict("pick_list_closed", "Yalnızca açık toplama listesi iptal edilebilir.");
        var ids       = list.ShipmentIds.ToList();
        var shipments = await db.Shipments.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        foreach (var s in shipments.Where(s => s.Status == ShipmentStatus.Picking)) {
            s.Status     = ShipmentStatus.Draft;
            s.PickListId = null;
        }

        list.Status = PickListStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return list;
    }
}

public sealed class PickListMapper(IApplicationDbContext db) {
    public async Task<PickListDto> ToDtoAsync(PickList list, CancellationToken ct) {
        var ids       = list.ShipmentIds.ToList();
        var warehouse = await db.Warehouses.AsNoTracking().Where(w => w.Id == list.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct);
        var shipments = await db.Shipments.AsNoTracking().Where(s => ids.Contains(s.Id)).OrderBy(s => s.Number)
                                .Select(s => new {
                                    s.Id, s.Number,
                                    OrderNumber  = db.SalesOrders.Where(o => o.Id == s.SalesOrderId).Select(o => o.Number).FirstOrDefault(),
                                    CustomerName = db.CurrentAccounts.Where(c => c.Id == s.CurrentAccountId).Select(c => c.Name).FirstOrDefault()
                                }).ToListAsync(ct);
        var productIds = list.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var variantIds = list.Lines.Where(l => l.VariantId != null).Select(l => l.VariantId!.Value).Distinct().ToList();
        var variants   = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var registry   = await db.Barcodes.AsNoTracking().Where(b => productIds.Contains(b.ProductId)).ToListAsync(ct);
        var unitIds    = registry.Where(b => b.Owner == BarcodeOwner.Unit).Select(b => b.OwnerId).ToList();
        var unitFactor = await db.ProductUnits.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Factor, ct);

        // Okutma eşleşmesi ürün kataloğundaki barkod arama kuralına uyar: varyantsız satırda ürün ve birim barkodları, varyantlı satırda yalnızca o varyantın barkodu.
        List<PickBarcodeDto> BarcodesOf(PickLine l) {
            var result = new List<PickBarcodeDto>();
            foreach (var b in registry.Where(b => b.ProductId == l.ProductId)) {
                if (l.VariantId is { } variantId) {
                    if (b.Owner == BarcodeOwner.Variant && b.OwnerId == variantId) result.Add(new PickBarcodeDto(b.Value, b.Owner, variantId, 1m));
                } else if (b.Owner == BarcodeOwner.Product) {
                    result.Add(new PickBarcodeDto(b.Value, b.Owner, null, 1m));
                } else if (b.Owner == BarcodeOwner.Unit) {
                    result.Add(new PickBarcodeDto(b.Value, b.Owner, null, unitFactor.GetValueOrDefault(b.OwnerId, 1m)));
                }
            }

            return result;
        }

        string Tracking(Guid productId) {
            if (!products.TryGetValue(productId, out var p) || !TrackingValidator.IsTracked(p) || p.IsBundle) return "none";
            return TrackingValidator.ModeOf(p) == TrackingMode.Serial ? "serial" : "lot";
        }

        var lines = list.Lines.OrderBy(l => l.Sort)
                        .Select(l => new PickLineDto(l.Id, l.Sort, l.WarehouseId, l.ProductId, l.VariantId, products.GetValueOrDefault(l.ProductId)?.Code ?? string.Empty, l.Description, l.Unit,
                                                     l.Factor, l.Quantity, l.PickedQuantity, l.Location, l.SuggestedLots, l.PickedLots, l.PickedSerials, l.Sources,
                                                     l.VariantId is { } v ? variants.GetValueOrDefault(v)?.Code : null,
                                                     l.VariantId is { } v2 && variants.TryGetValue(v2, out var variant) && variant.Values.Count > 0 ? string.Join(" / ", variant.Values.Values) : null,
                                                     Tracking(l.ProductId), BarcodesOf(l))).ToList();
        return new PickListDto(list.Id, list.Number, list.Status, list.BranchId, list.WarehouseId, warehouse ?? string.Empty,
                               shipments.Select(s => new PickListShipmentDto(s.Id, s.Number, s.OrderNumber ?? string.Empty, s.CustomerName ?? string.Empty)).ToList(), list.Version, lines);
    }
}
