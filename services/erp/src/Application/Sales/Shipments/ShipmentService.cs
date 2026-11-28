using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Documents;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Inventory.Tracking;
using ERP.Application.Inventory.Warehouses;
using ERP.Application.Invoicing;
using ERP.Application.Sales.Invoicing;
using ERP.Application.Sales.Orders;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;
using ERP.Domain.Shipping;

namespace ERP.Application.Sales.Shipments;

/// <summary>Sevkin kargo/araç bilgisi (Dispatch ucundan). Kargo kodu ayarlardaki firma koduyla doğrulanmış olmalıdır.</summary>
public sealed record DispatchInfo(string? CarrierCode, string? TrackingNo, DateTimeOffset At, string? Vehicle, string? Driver);

public static class ShipmentEvents {
    public const string Shipped = "shipment.shipped";
}

public static class ShipmentStock {
    /// <summary>Sevkin stok çıkış hareketlerinin kaynak türü (iptal bu kaynağı geri alır).</summary>
    public const string SourceType = "sales-shipment";
}

/// <summary>
///     Sevk yazımı ve iptali. Çağıran açık bir işlemde, siparişin kilidi alınmış olarak çağırır; servis işlem açmaz ya da onaylamaz.
///     Kilit sırası: (cari), sipariş, sonra tüm stok anahtarları tek çağrıda. Stok çıkışı sipariş rezervasyonunu tüketir (ConsumeAsync).
/// </summary>
public sealed class ShipmentService(
    IApplicationDbContext   db,
    ICurrentTenant          tenant,
    IStockService           stock,
    IOutbox                 outbox,
    DefaultWarehouseService defaults,
    ITenantSettings         settings,
    TimeProvider            clock
) {
    private static decimal Base(decimal quantity, decimal factor) {
        return Math.Round(quantity * factor, 6, MidpointRounding.AwayFromZero);
    }

    public static void Recalculate(SalesOrder order) {
        if (order.Status is not (SalesOrderStatus.Approved or SalesOrderStatus.PartiallyShipped or SalesOrderStatus.Shipped)) return;
        var counted = order.Lines.Where(l => l.Quantity > 0m).ToList();
        order.Status = counted.Count > 0 && counted.All(l => l.ShippedQuantity >= l.Quantity) ? SalesOrderStatus.Shipped
                       : counted.Any(l => l.ShippedQuantity > 0m) ? SalesOrderStatus.PartiallyShipped
                       : SalesOrderStatus.Approved;
    }

    /// <summary>Sevk edilebilecek açık miktarı olan satırlar (hızlı fatura için).</summary>
    public static List<ShipmentLineInput> OpenLines(SalesOrder order) {
        return order.Lines.OrderBy(l => l.Sort).Where(l => l.OpenQuantity > 0m).Select(l => new ShipmentLineInput(l.Id, l.OpenQuantity, null, null)).ToList();
    }

    private sealed record Wanted(SalesOrderLine Line, decimal Quantity, ShipmentLineInput Input);

    private sealed record WantedStock(SalesOrderLine Line, decimal Quantity, ShipmentLineInput Input, Product Product);

    /// <summary>
    ///     Siparişe göre sevk eder. <paramref name="order" /> izlenen, kilitli ve güncel yüklenmiş olmalıdır. <paramref name="draft" /> verilirse yeni sevk açılmaz:
    ///     o taslak/paketlenmiş sevk (satırları <paramref name="input" /> ile aynıdır) Shipped olur; açık miktar bu anda yeniden denetlenir. <paramref name="dispatch" />
    ///     kargo/araç bilgisidir (Dispatch ucu verir; P7 doğrudan sevkte yoktur). Kilit sırası: sipariş, belge sayaçları (SV, IR), sonra tüm stok anahtarları.
    /// </summary>
    public async Task<Shipment> ShipAsync(SalesOrder order, ShipmentInput input, CancellationToken ct, Shipment? draft = null, DispatchInfo? dispatch = null) {
        if (order.Status is not (SalesOrderStatus.Approved or SalesOrderStatus.PartiallyShipped))
            throw SalesErrors.Conflict("order_not_shippable", "Yalnızca onaylı ya da kısmen sevk edilmiş siparişler sevk edilebilir.");
        if (input.Lines.Count == 0) throw SalesErrors.Bad("lines_required", "Sevk için en az bir satır seçilmelidir.");

        // Aynı satır birden çok kez gelirse miktarlar toplanır (tek sevk satırı).
        var wanted = new List<Wanted>();
        foreach (var group in input.Lines.GroupBy(l => l.OrderLineId)) {
            var line     = order.Lines.FirstOrDefault(l => l.Id == group.Key) ?? throw SalesErrors.Bad("order_line_invalid", "Sevk satırı siparişe ait değil.");
            var quantity = group.Sum(g => g.Quantity);
            if (quantity <= 0m) throw SalesErrors.Bad("quantity_required", "Sevk miktarı sıfırdan büyük olmalıdır.");
            if (Math.Round(quantity, 6) != quantity) throw SalesErrors.Bad("quantity_scale", "Sevk miktarı en fazla 6 ondalık basamak içerebilir.");
            if (quantity > line.OpenQuantity)
                throw SalesErrors.Bad("ship_exceeds_open", $"\"{line.Description}\" için sevk edilebilecek en fazla miktar {line.OpenQuantity:0.######}.");
            var merged = group.Count() == 1
                             ? group.First()
                             : new ShipmentLineInput(group.Key, quantity, group.SelectMany(g => g.Lots ?? []).ToList(), group.SelectMany(g => g.Serials ?? []).ToList());
            wanted.Add(new Wanted(line, quantity, merged));
        }

        var productIds = wanted.Select(w => w.Line.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var lineIds    = wanted.Select(w => w.Line.Id).ToList();
        var reserved   = (await db.Reservations.AsNoTracking()
                                  .Where(r => r.SourceType == SalesStock.SourceType && r.SourceId == order.Id && lineIds.Contains(r.SourceLineId) && r.Status == ReservationStatus.Active)
                                  .ToListAsync(ct)).GroupBy(r => r.SourceLineId).ToDictionary(g => g.Key, g => g.ToList());

        // Başlık deposu: verilmediyse (hızlı fatura) rezervasyon deposu, o da yoksa şubenin varsayılan deposu.
        Warehouse warehouse;
        if (input.WarehouseId is { } wid) {
            warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == wid, ct) ?? throw new NotFoundException(wid.ToString(), "Warehouse");
            BranchGuard.Ensure(tenant, warehouse.BranchId);
            if (warehouse.BranchId != order.BranchId) throw SalesErrors.Bad("warehouse_branch_mismatch", "Depo, siparişin şubesine ait değil.");
            if (!warehouse.IsActive) throw SalesErrors.Conflict("warehouse_inactive", "Depo pasif.");
        } else {
            var first = reserved.Values.SelectMany(r => r).OrderBy(r => r.Id).FirstOrDefault();
            warehouse = first is not null
                            ? await db.Warehouses.AsNoTracking().FirstAsync(w => w.Id == first.WarehouseId, ct)
                            : await defaults.EnsureAsync(order.BranchId, ct);
        }

        var stockable = new List<WantedStock>();
        var bundles   = new List<WantedStock>();
        foreach (var w in wanted) {
            var product = products[w.Line.ProductId];
            if (product.Kind == ProductKind.Service) continue;
            if (product.IsBundle) {
                bundles.Add(new WantedStock(w.Line, w.Quantity, w.Input, product));
                continue;
            }

            if (!reserved.TryGetValue(w.Line.Id, out var rows) || rows.Count == 0)
                throw SalesErrors.Conflict("reservation_missing", $"\"{w.Line.Description}\" için aktif rezervasyon yok.");
            if (rows.Any(r => r.ExpectedReceiptSourceId != null))
                throw SalesErrors.Conflict("supply_pending", $"\"{w.Line.Description}\" için tedarik henüz tamamen gelmedi; mal kabulden sonra sevk edilebilir.");
            if (input.WarehouseId is not null && rows.Any(r => r.WarehouseId != warehouse.Id))
                throw SalesErrors.Bad("warehouse_mismatch", $"\"{w.Line.Description}\" başka bir depoda rezerve edilmiş; sevk deposu rezervasyon deposuyla aynı olmalıdır.");
            if (rows.Sum(r => r.Quantity) < Base(w.Quantity, w.Line.Factor))
                throw SalesErrors.Conflict("reservation_short", $"\"{w.Line.Description}\" için rezerve edilen miktar sevk miktarından az.");
            stockable.Add(new WantedStock(w.Line, w.Quantity, w.Input, product));
        }

        // Tüm stok anahtarları tek çağrıda (rezervasyon deposu x kalem; set ürünlerde bileşenler; takipli kalemlerde kalem kilidi).
        var keys = new List<(Guid, Guid, Guid?)>();
        foreach (var s in stockable) {
            foreach (var r in reserved[s.Line.Id]) keys.Add((r.WarehouseId, r.ProductId, r.VariantId));
            if (TrackingValidator.IsTracked(s.Product)) keys.Add(TrackingValidator.ItemKey(s.Product.Id));
        }

        var componentIds = bundles.SelectMany(b => b.Product.BundleComponents.Select(c => c.ProductId)).Distinct().ToList();
        var components   = componentIds.Count == 0 ? [] : await db.Products.AsNoTracking().Where(p => componentIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var b in bundles)
            foreach (var c in b.Product.BundleComponents) {
                keys.Add((warehouse.Id, c.ProductId, c.VariantId));
                if (components.TryGetValue(c.ProductId, out var cp) && TrackingValidator.IsTracked(cp)) keys.Add(TrackingValidator.ItemKey(cp.Id));
            }

        // Belge sayaçları stok kilitlerinden ÖNCE alınır (genel kilit sırası: ... sipariş, seri/sayaç, stok anahtarları).
        var date   = input.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
        var number = draft?.Number ?? await DocumentNumbers.NextAsync(db, "SV", date, ct);
        var irsaliye = await NextIrsaliyeAsync(order.BranchId, date, ct);

        if (keys.Count > 0) await db.LockStockAsync(keys, ct);

        var now = clock.GetUtcNow();
        Shipment shipment;
        if (draft is null) {
            shipment = new Shipment {
                Number = number, Status = ShipmentStatus.Shipped, SalesOrderId = order.Id, CurrentAccountId = order.CurrentAccountId,
                BranchId = order.BranchId, WarehouseId = warehouse.Id, Date = date, ShippedAt = now,
                DeliveryAddress = await ResolveAddressAsync(order.CurrentAccountId, input.DeliveryAddressId, ct),
                CargoNote = string.IsNullOrWhiteSpace(input.CargoNote) ? null : input.CargoNote.Trim(),
                Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim()
            };

            var sort = 1;
            foreach (var w in wanted.OrderBy(w => w.Line.Sort)) {
                shipment.Lines.Add(new ShipmentLine {
                    ShipmentId = shipment.Id, Sort = sort++, SalesOrderLineId = w.Line.Id, ProductId = w.Line.ProductId, VariantId = w.Line.VariantId, Description = w.Line.Description,
                    Unit = w.Line.Unit, Factor = w.Line.Factor, Quantity = w.Quantity, Width = w.Line.Width, Length = w.Line.Length, WarehouseId = warehouse.Id,
                    Lots = w.Input.Lots is { Count: > 0 } ? w.Input.Lots : null, Serials = InvoiceRules.NormalizeSerials(w.Input.Serials)
                });
            }

            db.Shipments.Add(shipment);
        } else {
            shipment           = draft;
            shipment.Status    = ShipmentStatus.Shipped;
            shipment.Date      = date;
            shipment.ShippedAt = now;
        }

        shipment.IrsaliyeNo  = irsaliye;
        shipment.DispatchAt  = dispatch?.At ?? now;
        shipment.CarrierCode = dispatch?.CarrierCode;
        shipment.TrackingNo  = dispatch?.TrackingNo;
        shipment.Vehicle     = dispatch?.Vehicle;
        shipment.Driver      = dispatch?.Driver;
        shipment.Attempts.Add(new DeliveryAttempt { ShipmentId = shipment.Id, AttemptNo = 1, SentAt = shipment.DispatchAt.Value });
        await db.SaveChangesAsync(ct);

        foreach (var s in stockable.OrderBy(s => s.Line.Sort)) {
            var lots = s.Input.Lots?.Select(l => new LotInput(l.LotNo, l.Expiry, l.Quantity, l.LotId)).ToList();
            await stock.ConsumeAsync(SalesStock.SourceType, order.Id, s.Line.Id, Base(s.Quantity, s.Line.Factor), ct, ShipmentStock.SourceType, shipment.Id, date,
                                     false, lots is { Count: > 0 } ? lots : null, InvoiceRules.NormalizeSerials(s.Input.Serials));
        }

        if (bundles.Count > 0) {
            var lines = bundles.OrderBy(b => b.Line.Sort)
                               .Select(b => new StockLine(warehouse.Id, b.Product.Id, b.Line.VariantId, Base(b.Quantity, b.Line.Factor), null, null, null, b.Line.Id)).ToList();
            await stock.IssueAsync(MovementType.SalesShipment, ShipmentStock.SourceType, shipment.Id, date, lines, ct);
        }

        foreach (var w in wanted) w.Line.ShippedQuantity += w.Quantity;
        Recalculate(order);
        outbox.Enqueue("sales.shipment-created", new { shipmentId = shipment.Id, number = shipment.Number, orderId = order.Id, orderNumber = order.Number });
        if (dispatch is not null) EnqueueShipped(shipment, 1, false);
        await db.SaveChangesAsync(ct);
        return shipment;
    }

    /// <summary>Müşteri bildirimi ve dış tüketiciler için "sevk edildi" olayı (ilk çıkış ya da yeniden gönderim).</summary>
    public void EnqueueShipped(Shipment shipment, int attemptNo, bool resend) {
        outbox.Enqueue(ShipmentEvents.Shipped, new {
            shipmentId = shipment.Id, number = shipment.Number, orderId = shipment.SalesOrderId, currentAccountId = shipment.CurrentAccountId, carrierCode = shipment.CarrierCode,
            trackingNo = shipment.TrackingNo, irsaliyeNo = shipment.IrsaliyeNo, attemptNo, resend
        });
    }

    /// <summary>
    ///     Şube bazında irsaliye numarası: IR{yyyy}{000001}. Sayaç anahtarı şubeyi içerir; e-İrsaliye açıksa numarayı P10 verir (null döner).
    ///     Sayaç satırı işlem sonuna kadar kilitli kalır, bu yüzden stok kilitlerinden önce çağrılmalıdır.
    /// </summary>
    private async Task<string?> NextIrsaliyeAsync(Guid branchId, DateOnly date, CancellationToken ct) {
        if ((await settings.GetAsync<EBelgeSettings>(ct)).EIrsaliyeEnabled) return null;
        var n = await db.NextDocumentNumberAsync($"IR{date.Year}:{branchId:N}", ct);
        return $"IR{date.Year}{n:000000}";
    }

    /// <summary>
    ///     Taslak sevk (toplama-paketleme akışı): stok çıkışı ve sevk edilen miktar sevk anında (Dispatch) işlenir; taslak yalnızca açık miktarı ayırır.
    ///     Aynı siparişin diğer canlı taslakları (Draft/Picking/Packed) açık miktardan düşülür. Sipariş kilidi çağıranda alınmış olmalıdır.
    /// </summary>
    public async Task<Shipment> CreateDraftAsync(SalesOrder order, ShipmentInput input, CancellationToken ct) {
        if (order.Status is not (SalesOrderStatus.Approved or SalesOrderStatus.PartiallyShipped))
            throw SalesErrors.Conflict("order_not_shippable", "Yalnızca onaylı ya da kısmen sevk edilmiş siparişler sevk edilebilir.");
        if (input.Lines.Count == 0) throw SalesErrors.Bad("lines_required", "Sevk için en az bir satır seçilmelidir.");

        var pending = await PendingByLineAsync(order, ct);

        var wanted = new List<Wanted>();
        foreach (var group in input.Lines.GroupBy(l => l.OrderLineId)) {
            var line     = order.Lines.FirstOrDefault(l => l.Id == group.Key) ?? throw SalesErrors.Bad("order_line_invalid", "Sevk satırı siparişe ait değil.");
            var quantity = group.Sum(g => g.Quantity);
            if (quantity <= 0m) throw SalesErrors.Bad("quantity_required", "Sevk miktarı sıfırdan büyük olmalıdır.");
            if (Math.Round(quantity, 6) != quantity) throw SalesErrors.Bad("quantity_scale", "Sevk miktarı en fazla 6 ondalık basamak içerebilir.");
            var available = line.OpenQuantity - pending.GetValueOrDefault(line.Id);
            if (quantity > available)
                throw SalesErrors.Bad("ship_exceeds_open", $"\"{line.Description}\" için sevk edilebilecek en fazla miktar {Math.Max(0m, available):0.######}.");
            var merged = group.Count() == 1
                             ? group.First()
                             : new ShipmentLineInput(group.Key, quantity, group.SelectMany(g => g.Lots ?? []).ToList(), group.SelectMany(g => g.Serials ?? []).ToList());
            wanted.Add(new Wanted(line, quantity, merged));
        }

        var lineIds  = wanted.Select(w => w.Line.Id).ToList();
        var reserved = (await db.Reservations.AsNoTracking()
                                .Where(r => r.SourceType == SalesStock.SourceType && r.SourceId == order.Id && lineIds.Contains(r.SourceLineId) && r.Status == ReservationStatus.Active)
                                .ToListAsync(ct)).GroupBy(r => r.SourceLineId).ToDictionary(g => g.Key, g => g.ToList());
        var productIds = wanted.Select(w => w.Line.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        Warehouse warehouse;
        if (input.WarehouseId is { } wid) {
            warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == wid, ct) ?? throw new NotFoundException(wid.ToString(), "Warehouse");
            BranchGuard.Ensure(tenant, warehouse.BranchId);
            if (warehouse.BranchId != order.BranchId) throw SalesErrors.Bad("warehouse_branch_mismatch", "Depo, siparişin şubesine ait değil.");
            if (!warehouse.IsActive) throw SalesErrors.Conflict("warehouse_inactive", "Depo pasif.");
        } else {
            var first = reserved.Values.SelectMany(r => r).OrderBy(r => r.Id).FirstOrDefault();
            warehouse = first is not null
                            ? await db.Warehouses.AsNoTracking().FirstAsync(w => w.Id == first.WarehouseId, ct)
                            : await defaults.EnsureAsync(order.BranchId, ct);
        }

        foreach (var w in wanted) {
            var product = products[w.Line.ProductId];
            if (product.Kind == ProductKind.Service || product.IsBundle) continue;
            if (!reserved.TryGetValue(w.Line.Id, out var rows) || rows.Count == 0)
                throw SalesErrors.Conflict("reservation_missing", $"\"{w.Line.Description}\" için aktif rezervasyon yok.");
            if (rows.Any(r => r.ExpectedReceiptSourceId != null))
                throw SalesErrors.Conflict("supply_pending", $"\"{w.Line.Description}\" için tedarik henüz tamamen gelmedi; mal kabulden sonra sevk edilebilir.");
            if (input.WarehouseId is not null && rows.Any(r => r.WarehouseId != warehouse.Id))
                throw SalesErrors.Bad("warehouse_mismatch", $"\"{w.Line.Description}\" başka bir depoda rezerve edilmiş; sevk deposu rezervasyon deposuyla aynı olmalıdır.");
        }

        var date     = input.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
        var shipment = new Shipment {
            Number = await DocumentNumbers.NextAsync(db, "SV", date, ct), Status = ShipmentStatus.Draft, SalesOrderId = order.Id, CurrentAccountId = order.CurrentAccountId,
            BranchId = order.BranchId, WarehouseId = warehouse.Id, Date = date,
            DeliveryAddress = await ResolveAddressAsync(order.CurrentAccountId, input.DeliveryAddressId, ct),
            CargoNote = string.IsNullOrWhiteSpace(input.CargoNote) ? null : input.CargoNote.Trim(),
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim()
        };
        var sort = 1;
        foreach (var w in wanted.OrderBy(w => w.Line.Sort)) {
            shipment.Lines.Add(new ShipmentLine {
                ShipmentId = shipment.Id, Sort = sort++, SalesOrderLineId = w.Line.Id, ProductId = w.Line.ProductId, VariantId = w.Line.VariantId, Description = w.Line.Description,
                Unit = w.Line.Unit, Factor = w.Line.Factor, Quantity = w.Quantity, Width = w.Line.Width, Length = w.Line.Length, WarehouseId = warehouse.Id,
                Lots = w.Input.Lots is { Count: > 0 } ? w.Input.Lots : null, Serials = InvoiceRules.NormalizeSerials(w.Input.Serials)
            });
        }

        db.Shipments.Add(shipment);
        await db.SaveChangesAsync(ct);
        return shipment;
    }

    private static readonly ShipmentStatus[] LiveStatuses = [ShipmentStatus.Draft, ShipmentStatus.Picking, ShipmentStatus.Packed];

    /// <summary>Siparişin canlı (Draft/Picking/Packed) sevklerinde sipariş satırı başına ayrılmış miktar.</summary>
    private async Task<Dictionary<Guid, decimal>> PendingByLineAsync(SalesOrder order, CancellationToken ct) {
        var live = LiveStatuses;
        return (await db.ShipmentLines.AsNoTracking()
                        .Where(l => db.Shipments.Any(s => s.Id == l.ShipmentId && s.SalesOrderId == order.Id && live.Contains(s.Status)))
                        .Select(l => new { l.SalesOrderLineId, l.Quantity }).ToListAsync(ct))
               .GroupBy(l => l.SalesOrderLineId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
    }

    /// <summary>Sevki iptal eder: stok geri alınır, rezervasyon yeniden aktif olur (stok kontrolü yapılmaz). Sipariş kilidi çağıranda alınmış olmalıdır.</summary>
    public async Task CancelAsync(SalesOrder order, Shipment shipment, string reason, CancellationToken ct) {
        // Taslak sevk: stok hareketi yok, yalnızca açık miktar ayrımı bırakılır (durum makinesi: Draft -> Cancelled).
        if (shipment.Status == ShipmentStatus.Draft) {
            shipment.Status       = ShipmentStatus.Cancelled;
            shipment.CancelReason = reason.Trim();
            await db.SaveChangesAsync(ct);
            return;
        }

        if (shipment.Status != ShipmentStatus.Shipped)
            throw SalesErrors.Conflict("shipment_not_cancellable", shipment.Status == ShipmentStatus.Cancelled ? "Sevk zaten iptal edilmiş." : "Yalnızca sevk edilmiş (teslim edilmemiş) sevkler iptal edilebilir.");
        if (order.Status is SalesOrderStatus.Closed or SalesOrderStatus.Cancelled)
            throw SalesErrors.Conflict("order_not_open", "Kapatılmış ya da iptal edilmiş siparişin sevki iptal edilemez.");

        // Sevkin faturasına aktif iade belgesi bağlıysa sevk iptal edilemez (iade edilen mal stoğa ikinci kez girerdi). Sipariş kilidi altında okunur;
        // iade kesimi de sipariş kilidini alır.
        var shipmentLineIds = shipment.Lines.Select(l => l.Id).ToList();
        var returnedInvoices = db.InvoiceLines.Where(l => l.SourceLineId != null && shipmentLineIds.Contains(l.SourceLineId.Value)).Select(l => l.InvoiceId);
        if (await db.SalesReturns.AnyAsync(r => r.Status != SalesReturnStatus.Cancelled && returnedInvoices.Contains(r.InvoiceId), ct))
            throw SalesErrors.Conflict("has_returns", "Sevkin faturasına bağlı iade belgesi var; önce iadeyi iptal edin.");

        if (await ShipmentInvoiceLinks.IsInvoicedAsync(db, shipment.Lines.Select(l => l.Id).ToList(), ct) || shipment.Lines.Any(l => l.InvoicedQuantity > 0m))
            throw SalesErrors.Conflict("shipment_invoiced", "Faturaya bağlı sevk iptal edilemez; önce faturayı iptal edin ya da taslağı silin.");

        await ReverseStockAsync(order, shipment, ct);

        shipment.Status       = ShipmentStatus.Cancelled;
        shipment.CancelReason = reason.Trim();
        Recalculate(order);
        outbox.Enqueue("sales.shipment-cancelled", new { shipmentId = shipment.Id, number = shipment.Number, orderId = order.Id, reason = shipment.CancelReason });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    ///     Sevk çıkışını geri alır: stok hareketleri tersine yazılır, rezervasyonlar yeniden aktif olur, sipariş satırlarının sevk edilen miktarı düşer. Stok kilitleri
    ///     burada tek çağrıda alınır (sipariş kilidi çağıranda alınmış olmalıdır). Durum ve olay çağıranındır.
    /// </summary>
    private async Task ReverseStockAsync(SalesOrder order, Shipment shipment, CancellationToken ct) {
        var moved = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == ShipmentStock.SourceType && m.SourceId == shipment.Id && m.Quantity != 0m)
                            .Select(m => new { m.WarehouseId, m.ProductId, m.VariantId, Tracked = m.LotLines.Any() }).Distinct().ToListAsync(ct);
        var reservedKeys = await db.Reservations.AsNoTracking().Where(r => r.SourceType == SalesStock.SourceType && r.SourceId == order.Id && r.Status == ReservationStatus.Consumed)
                                   .Select(r => new { r.WarehouseId, r.ProductId, r.VariantId }).Distinct().ToListAsync(ct);
        var keys = moved.Select(m => (m.WarehouseId, m.ProductId, m.VariantId))
                        .Concat(moved.Where(m => m.Tracked).Select(m => TrackingValidator.ItemKey(m.ProductId)))
                        .Concat(reservedKeys.Select(k => (k.WarehouseId, k.ProductId, k.VariantId))).ToList();
        if (keys.Count > 0) await db.LockStockAsync(keys, ct);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
        if (moved.Count > 0) await stock.ReverseAsync(ShipmentStock.SourceType, shipment.Id, today, ct);

        foreach (var line in shipment.Lines) {
            var orderLine = order.Lines.First(l => l.Id == line.SalesOrderLineId);
            await stock.RestoreConsumedAsync(SalesStock.SourceType, order.Id, orderLine.Id, Base(line.Quantity, line.Factor), ct);
            orderLine.ShippedQuantity = Math.Max(0m, orderLine.ShippedQuantity - line.Quantity);
        }
    }

    /// <summary>
    ///     Teslim edilemeyen (DeliveryFailed) faturasız sevkin iadesi: stok çıkışı geri alınır, sipariş satırı yeniden açılır (P7 iptal yolu), sevk Returned olur.
    ///     Faturalı sevk burada işlenmez: o durumda P7 iade belgesi kesilir (ReturnShipmentCommand). Sipariş kilidi çağıranda alınmış olmalıdır.
    /// </summary>
    public async Task ReturnUninvoicedAsync(SalesOrder order, Shipment shipment, string reason, CancellationToken ct) {
        if (order.Status is SalesOrderStatus.Closed or SalesOrderStatus.Cancelled)
            throw SalesErrors.Conflict("order_not_open", "Kapatılmış ya da iptal edilmiş siparişin sevki iade edilemez.");

        await ReverseStockAsync(order, shipment, ct);
        shipment.Status       = ShipmentStatus.Returned;
        shipment.CancelReason = reason.Trim();
        Recalculate(order);
        outbox.Enqueue("shipment.returned", new { shipmentId = shipment.Id, number = shipment.Number, orderId = order.Id, reason = shipment.CancelReason, returnId = (Guid?)null });
        await db.SaveChangesAsync(ct);
    }

    private async Task<AddressSnapshot?> ResolveAddressAsync(Guid cariId, Guid? addressId, CancellationToken ct) {
        var cari      = await db.CurrentAccounts.AsNoTracking().FirstAsync(c => c.Id == cariId, ct);
        var addresses = await db.CurrentAccountAddresses.AsNoTracking().Where(a => a.CurrentAccountId == cariId).ToListAsync(ct);
        Domain.CurrentAccounts.CurrentAccountAddress? address;
        if (addressId is { } id)
            address = addresses.FirstOrDefault(a => a.Id == id) ?? throw SalesErrors.Bad("address_invalid", "Teslimat adresi müşteriye ait değil.");
        else
            address = addresses.FirstOrDefault(a => a.IsDefaultShipping) ?? addresses.FirstOrDefault(a => a.IsShipping) ?? addresses.FirstOrDefault(a => a.IsDefaultBilling)
                      ?? addresses.FirstOrDefault();

        return address is null ? null : new AddressSnapshot(cari.Name, cari.TaxNumber, cari.TaxOffice, address.Line, address.City, address.District, address.Country, cari.Email);
    }
}
