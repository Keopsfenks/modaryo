using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Ledger;

/// <summary>Bkz. <see cref="IStockService" />. Kilit sırası: önce tüm anahtarlar (sıralı), sonra okuma ve yazma.</summary>
internal sealed class StockService(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IPeriodLock           periodLock,
    StockLedger           ledger
) : IStockService {
    public const string BackToBackModule = "siparise-gore-tedarik";

    private const string ReversalPrefix = "reversal:";

    private sealed record Work(
        Warehouse Warehouse,
        Product   Product,
        Guid?     VariantId,
        decimal   Quantity,
        decimal?  UnitCost,
        Guid?     LineId,
        bool      Exempt,
        MovementType Type
    ) {
        public string Label => $"{Product.Code} - {Product.Name}";
    }

    // ---- ortak yardımcılar -----------------------------------------------------------------------------------

    private void RequireBackToBack(bool exempt) {
        if (exempt && !tenant.HasModule(BackToBackModule)) throw InventoryErrors.ModuleDisabled();
    }

    private static void ValidateQuantity(decimal quantity) {
        if (quantity <= 0m || decimal.Round(quantity, 6) != quantity)
            throw new AppException(400, "invalid_quantity", "Miktar sıfırdan büyük olmalı ve en fazla 6 ondalık içermelidir.");
    }

    private async Task<Dictionary<Guid, Warehouse>> LoadWarehousesAsync(IEnumerable<Guid> ids, CancellationToken ct,
                                                                          bool requireActive = true) {
        var wanted     = ids.Distinct().ToList();
        var warehouses = await db.Warehouses.Where(w => wanted.Contains(w.Id)).ToListAsync(ct);
        foreach (var id in wanted)
            if (warehouses.All(w => w.Id != id))
                throw new NotFoundException(id.ToString(), "Warehouse");

        foreach (var warehouse in warehouses) {
            BranchGuard.Ensure(tenant, warehouse.BranchId);
            if (requireActive && !warehouse.IsActive) throw InventoryErrors.WarehouseInactive(warehouse.Name);
        }

        return warehouses.ToDictionary(w => w.Id);
    }

    private async Task<Dictionary<Guid, Product>> LoadProductsAsync(IEnumerable<Guid> ids, CancellationToken ct) {
        var wanted   = ids.Distinct().ToList();
        var products = await db.Products.Where(p => wanted.Contains(p.Id)).ToListAsync(ct);
        foreach (var id in wanted)
            if (products.All(p => p.Id != id))
                throw new NotFoundException(id.ToString(), "Product");

        foreach (var product in products)
            if (product.Kind == ProductKind.Service)
                throw InventoryErrors.ItemNotStockable($"{product.Code} - {product.Name}");

        return products.ToDictionary(p => p.Id);
    }

    private Task LockAsync(IEnumerable<(Guid, Guid, Guid?)> keys, CancellationToken ct) {
        return db.LockStockAsync(keys, ct);
    }

    // ---- giriş / çıkış ---------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<Guid>> ReceiveAsync(MovementType type, string sourceType, Guid sourceId, DateOnly date,
                                                        IReadOnlyList<StockLine> lines, CancellationToken ct) {
        foreach (var line in lines) {
            ValidateQuantity(line.BaseQuantity);
            if (line.UnitCost is < 0m) throw new AppException(400, "invalid_cost", "Birim maliyet negatif olamaz.");
        }

        var warehouses = await LoadWarehousesAsync(lines.Select(l => l.WarehouseId), ct);
        var products   = await LoadProductsAsync(lines.Select(l => l.ProductId), ct);
        foreach (var product in products.Values)
            if (product.IsBundle)
                throw InventoryErrors.BundleInbound();

        await periodLock.EnsureOpenAsync(date, ct);
        await LockAsync(lines.Select(l => (l.WarehouseId, l.ProductId, l.VariantId)), ct);

        var context = await ledger.BeginAsync(ct);
        var ids     = new List<Guid>();
        foreach (var line in lines) {
            var movement = await ledger.InboundAsync(context, warehouses[line.WarehouseId], line.ProductId, line.VariantId, type, date,
                                                     line.BaseQuantity, line.UnitCost, sourceType, sourceId, line.SourceLineId, null, ct);
            ids.Add(movement.Id);
        }

        await db.SaveChangesAsync(ct);
        return ids;
    }

    public async Task<IReadOnlyList<Guid>> IssueAsync(MovementType type, string sourceType, Guid sourceId, DateOnly date,
                                                      IReadOnlyList<StockLine> lines, CancellationToken ct) {
        foreach (var line in lines) {
            ValidateQuantity(line.BaseQuantity);
            RequireBackToBack(line.ExemptFromStockCheck);
        }

        var warehouses = await LoadWarehousesAsync(lines.Select(l => l.WarehouseId), ct);
        var products   = await LoadProductsAsync(lines.Select(l => l.ProductId), ct);

        // Set ürün satırları bileşenlerine açılır.
        var work = new List<Work>();
        foreach (var line in lines) {
            var product = products[line.ProductId];
            if (!product.IsBundle) {
                work.Add(new Work(warehouses[line.WarehouseId], product, line.VariantId, line.BaseQuantity, null, line.SourceLineId,
                                  line.ExemptFromStockCheck, type));
                continue;
            }

            var components = await LoadProductsAsync(product.BundleComponents.Select(c => c.ProductId), ct);
            foreach (var component in product.BundleComponents)
                work.Add(new Work(warehouses[line.WarehouseId], components[component.ProductId], component.VariantId,
                                  line.BaseQuantity * component.Quantity, null, line.SourceLineId, line.ExemptFromStockCheck,
                                  MovementType.BundleIssue));
        }

        await periodLock.EnsureOpenAsync(date, ct);
        await LockAsync(work.Select(w => (w.Warehouse.Id, w.Product.Id, w.VariantId)), ct);

        var context = await ledger.BeginAsync(ct);
        var ids     = new List<Guid>();
        foreach (var item in work) ids.Add(await IssueOneAsync(context, item, date, sourceType, sourceId, ct));

        await db.SaveChangesAsync(ct);
        return ids;
    }

    private async Task<Guid> IssueOneAsync(LedgerContext context, Work item, DateOnly date, string sourceType, Guid sourceId,
                                           CancellationToken ct) {
        var watch    = await ledger.WatchMinAsync(item.Warehouse, item.Product.Id, item.VariantId, ct);
        var movement = await ledger.OutboundAsync(context, item.Warehouse, item.Product.Id, item.VariantId, item.Type, date,
                                                  item.Quantity, sourceType, sourceId, item.LineId, null, item.Exempt, item.Label,
                                                  null, null, ct);
        await ledger.CheckMinAsync(watch, item.Warehouse, ct);
        return movement.Id;
    }

    // ---- iptal -----------------------------------------------------------------------------------------------

    public async Task ReverseAsync(string sourceType, Guid sourceId, DateOnly date, CancellationToken ct) {
        await periodLock.EnsureOpenAsync(date, ct);

        // Kilit için anahtarları (izlemesiz) oku; kilit alındıktan sonra bekleyenleri yeniden hesapla:
        // bu arada başka bir istek aynı kaynağı iptal etmiş olabilir.
        var keys = await db.StockMovements.AsNoTracking()
                           .Where(m => m.SourceType == sourceType && m.SourceId == sourceId && m.Quantity != 0m)
                           .Select(m => new { m.WarehouseId, m.ProductId, m.VariantId }).Distinct().ToListAsync(ct);
        if (keys.Count == 0) return;

        await LockAsync(keys.Select(k => (k.WarehouseId, k.ProductId, k.VariantId)), ct);

        var movements = await db.StockMovements.AsNoTracking()
                                .Where(m => m.SourceType == sourceType && m.SourceId == sourceId)
                                .Include(m => m.LotLines).OrderBy(m => m.Sequence).ToListAsync(ct);
        var reversed = movements.Where(m => IsReversal(m)).Select(m => m.Note![ReversalPrefix.Length..]).ToHashSet();
        var pending  = movements.Where(m => m.Quantity != 0m && !IsReversal(m) && !reversed.Contains(m.Id.ToString()))
                                .OrderByDescending(m => m.Sequence).ToList();
        if (pending.Count == 0) return;

        var warehouses = await LoadWarehousesAsync(pending.Select(m => m.WarehouseId), ct, requireActive: false);
        var productIds = pending.Select(m => m.ProductId).Distinct().ToList();
        var products   = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var context = await ledger.BeginAsync(ct);
        foreach (var original in pending) {
            var warehouse = warehouses[original.WarehouseId];
            var note      = ReversalPrefix + original.Id;
            var product   = products[original.ProductId];
            var label     = $"{product.Code} - {product.Name}";
            StockMovement written;
            if (original.Quantity > 0m) {
                var watch = await ledger.WatchMinAsync(warehouse, original.ProductId, original.VariantId, ct);
                written = await ledger.OutboundAsync(context, warehouse, original.ProductId, original.VariantId, original.Type, date,
                                                     original.Quantity, sourceType, sourceId, original.SourceLineId, note, false, label,
                                                     original.UnitCost, original.Id, ct);
                await ledger.CheckMinAsync(watch, warehouse, ct);
            } else {
                written = await ledger.InboundAsync(context, warehouse, original.ProductId, original.VariantId, original.Type, date,
                                                    -original.Quantity, original.UnitCost, sourceType, sourceId, original.SourceLineId,
                                                    note, ct);
            }

            foreach (var lot in original.LotLines)
                written.LotLines.Add(new MovementLotLine { LotId = lot.LotId, SerialId = lot.SerialId, Quantity = -lot.Quantity });
        }

        await db.SaveChangesAsync(ct);
    }

    private static bool IsReversal(StockMovement movement) {
        return movement.Note != null && movement.Note.StartsWith(ReversalPrefix, StringComparison.Ordinal);
    }

    // ---- rezervasyon -----------------------------------------------------------------------------------------

    public async Task ReserveAsync(string sourceType, Guid sourceId, IReadOnlyList<ReserveLine> lines, CancellationToken ct) {
        foreach (var line in lines) {
            ValidateQuantity(line.BaseQuantity);
            RequireBackToBack(line.ExemptFromStockCheck);
        }

        var warehouses = await LoadWarehousesAsync(lines.Select(l => l.WarehouseId), ct);
        var products   = await LoadProductsAsync(lines.Select(l => l.ProductId), ct);
        foreach (var product in products.Values)
            if (product.IsBundle)
                throw InventoryErrors.BundleNotReservable();

        await LockAsync(lines.Select(l => (l.WarehouseId, l.ProductId, l.VariantId)), ct);

        var context = await ledger.BeginAsync(ct);
        foreach (var line in lines) {
            var warehouse = warehouses[line.WarehouseId];
            var product   = products[line.ProductId];
            var watch     = await ledger.WatchMinAsync(warehouse, line.ProductId, line.VariantId, ct);

            if (!line.ExemptFromStockCheck && !context.AllowNegative) {
                var available = await ledger.AvailableAsync(warehouse, line.ProductId, line.VariantId, ct);
                if (available < line.BaseQuantity) throw InventoryErrors.InsufficientStock($"{product.Code} - {product.Name}");
            }

            db.Reservations.Add(new Reservation {
                WarehouseId = line.WarehouseId, ProductId = line.ProductId, VariantId = line.VariantId, Quantity = line.BaseQuantity,
                SourceType  = sourceType, SourceId = sourceId, SourceLineId = line.SourceLineId,
                ExpectedReceiptSourceId = line.ExpectedReceiptSourceId
            });
            await db.SaveChangesAsync(ct);
            await ledger.CheckMinAsync(watch, warehouse, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Kaynak satırının rezervasyon anahtarlarını kilitler; çağıran sonra satırları yeniden okur.</summary>
    private async Task LockReservationsAsync(string sourceType, Guid sourceId, Guid? lineId, CancellationToken ct) {
        var keys = await db.Reservations.AsNoTracking()
                           .Where(r => r.SourceType == sourceType && r.SourceId == sourceId && (lineId == null || r.SourceLineId == lineId))
                           .Select(r => new { r.WarehouseId, r.ProductId, r.VariantId }).Distinct().ToListAsync(ct);
        await LockAsync(keys.Select(k => (k.WarehouseId, k.ProductId, k.VariantId)), ct);
    }

    public async Task ReleaseAsync(string sourceType, Guid sourceId, Guid? sourceLineId, CancellationToken ct) {
        await LockReservationsAsync(sourceType, sourceId, sourceLineId, ct);
        var rows = await db.Reservations
                           .Where(r => r.SourceType == sourceType && r.SourceId == sourceId && r.Status == ReservationStatus.Active
                                       && (sourceLineId == null || r.SourceLineId == sourceLineId))
                           .ToListAsync(ct);
        foreach (var row in rows) {
            BranchGuard.Ensure(tenant, (await db.Warehouses.FirstAsync(w => w.Id == row.WarehouseId, ct)).BranchId);
            row.Status = ReservationStatus.Released;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task ConsumeAsync(string sourceType, Guid sourceId, Guid sourceLineId, decimal baseQuantity, CancellationToken ct,
                                   string? movementSourceType = null, Guid? movementSourceId = null, DateOnly? date = null,
                                   bool exemptFromStockCheck = false) {
        ValidateQuantity(baseQuantity);
        RequireBackToBack(exemptFromStockCheck);

        await LockReservationsAsync(sourceType, sourceId, sourceLineId, ct);
        var rows = await db.Reservations
                           .Where(r => r.SourceType == sourceType && r.SourceId == sourceId && r.SourceLineId == sourceLineId
                                       && r.Status == ReservationStatus.Active)
                           .OrderBy(r => r.Id).ToListAsync(ct);
        // Rezervasyon yoksa depo/kalem bilinmez; bu durumda çağıran IssueAsync kullanmalıdır.
        if (rows.Count == 0) throw new NotFoundException(sourceLineId.ToString(), "Reservation");

        var first      = rows[0];
        var warehouses = await LoadWarehousesAsync(rows.Select(r => r.WarehouseId), ct);
        var product    = (await LoadProductsAsync([first.ProductId], ct))[first.ProductId];
        var when       = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await periodLock.EnsureOpenAsync(when, ct);

        // Rezervasyon (tek depo x kalem varsayılır: bir kaynak satırı tek depoda rezerve edilir) tüketilir.
        var left = baseQuantity;
        foreach (var row in rows) {
            if (left <= 0m) break;
            if (row.Quantity <= left) {
                row.Status = ReservationStatus.Consumed;
                left      -= row.Quantity;
            } else {
                db.Reservations.Add(new Reservation {
                    WarehouseId = row.WarehouseId, ProductId = row.ProductId, VariantId = row.VariantId, Quantity = left,
                    SourceType  = row.SourceType, SourceId = row.SourceId, SourceLineId = row.SourceLineId,
                    Status      = ReservationStatus.Consumed, ExpectedReceiptSourceId = row.ExpectedReceiptSourceId
                });
                row.Quantity -= left;
                left          = 0m;
            }
        }

        await db.SaveChangesAsync(ct);

        var context = await ledger.BeginAsync(ct);
        var item = new Work(warehouses[first.WarehouseId], product, first.VariantId, baseQuantity, null, sourceLineId,
                            exemptFromStockCheck, MovementType.SalesShipment);
        await IssueOneAsync(context, item, when, movementSourceType ?? sourceType, movementSourceId ?? sourceId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RestoreConsumedAsync(string sourceType, Guid sourceId, Guid sourceLineId, decimal baseQuantity, CancellationToken ct) {
        ValidateQuantity(baseQuantity);
        await LockReservationsAsync(sourceType, sourceId, sourceLineId, ct);
        var rows = await db.Reservations
                           .Where(r => r.SourceType == sourceType && r.SourceId == sourceId && r.SourceLineId == sourceLineId
                                       && r.Status == ReservationStatus.Consumed)
                           .OrderByDescending(r => r.Id).ToListAsync(ct);
        var left = baseQuantity;
        foreach (var row in rows) {
            if (left <= 0m) break;
            if (row.Quantity <= left) {
                row.Status = ReservationStatus.Active;
                left      -= row.Quantity;
            } else {
                db.Reservations.Add(new Reservation {
                    WarehouseId = row.WarehouseId, ProductId = row.ProductId, VariantId = row.VariantId, Quantity = left,
                    SourceType  = row.SourceType, SourceId = row.SourceId, SourceLineId = row.SourceLineId,
                    Status      = ReservationStatus.Active, ExpectedReceiptSourceId = row.ExpectedReceiptSourceId
                });
                row.Quantity -= left;
                left          = 0m;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RetargetExpectedAsync(Guid expectedReceiptSourceId, Guid warehouseId, decimal baseQuantity, CancellationToken ct,
                                            Guid? productId = null, Guid? variantId = null) {
        ValidateQuantity(baseQuantity);
        var target = (await LoadWarehousesAsync([warehouseId], ct))[warehouseId];

        IQueryable<Reservation> Matching() {
            var query = db.Reservations.Where(r => r.ExpectedReceiptSourceId == expectedReceiptSourceId
                                                   && r.Status == ReservationStatus.Active);
            return productId is null ? query : query.Where(r => r.ProductId == productId && r.VariantId == variantId);
        }

        var keys = await Matching().AsNoTracking().Select(r => new { r.WarehouseId, r.ProductId, r.VariantId }).Distinct().ToListAsync(ct);
        await LockAsync(keys.Select(k => (k.WarehouseId, k.ProductId, k.VariantId))
                            .Concat(keys.Select(k => (warehouseId, k.ProductId, k.VariantId))), ct);

        var rows = await Matching().OrderBy(r => r.Id).ToListAsync(ct);
        var left = baseQuantity;
        foreach (var row in rows) {
            if (left <= 0m) break;
            if (row.Quantity <= left) {
                left                   -= row.Quantity;
                row.WarehouseId         = target.Id;
                row.ExpectedReceiptSourceId = null;
            } else {
                db.Reservations.Add(new Reservation {
                    WarehouseId = target.Id, ProductId = row.ProductId, VariantId = row.VariantId, Quantity = left,
                    SourceType  = row.SourceType, SourceId = row.SourceId, SourceLineId = row.SourceLineId
                });
                row.Quantity -= left;
                left          = 0m;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ---- maliyet düzeltmesi ----------------------------------------------------------------------------------

    public async Task CorrectCostAsync(Guid warehouseId, Guid productId, Guid? variantId, DateOnly date, Guid inboundMovementId,
                                       decimal deltaTotalTl, string sourceType, Guid sourceId, CancellationToken ct) {
        var warehouse = (await LoadWarehousesAsync([warehouseId], ct))[warehouseId];
        await periodLock.EnsureOpenAsync(date, ct);
        await LockAsync([(warehouseId, productId, variantId)], ct);

        var inbound = await db.StockMovements.AsNoTracking()
                              .FirstOrDefaultAsync(m => m.Id == inboundMovementId && m.WarehouseId == warehouseId && m.ProductId == productId
                                                        && m.VariantId == variantId && m.Quantity > 0m, ct)
                      ?? throw new NotFoundException(inboundMovementId.ToString(), "StockMovement");

        var context = await ledger.BeginAsync(ct);
        await ledger.CorrectCostAsync(context, warehouse, inbound, date, deltaTotalTl, sourceType, sourceId, ct);
        await db.SaveChangesAsync(ct);
    }
}
