using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory.Ledger;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Tracking;

/// <summary>
///     Seri/lot kuralları. Şekil denetimi (ValidateInbound/ValidateOutbound) kilit ve yazımdan önce çalışır;
///     Apply* yöntemleri hareket yazıldıktan sonra lot/seri satırlarını ve seri konumunu işler. Lot ve seri oluşturma/taşıma,
///     depo x kalem kilidine ek olarak kalem düzeyinde kilit ister (<see cref="ItemKey" />): bir seri aynı anda tek depoda olabilir.
/// </summary>
internal sealed class TrackingValidator(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) {
    public const string Module = "seri-lot";

    public static bool IsTracked(Product product) {
        return product.TracksSerialLot;
    }

    public static TrackingMode ModeOf(Product product) {
        return product.TrackingMode ?? TrackingMode.Lot;
    }

    /// <summary>Kalem düzeyi kilit anahtarı (depo yerine boş kimlik).</summary>
    public static (Guid WarehouseId, Guid ProductId, Guid? VariantId) ItemKey(Guid productId, Guid? variantId) {
        return (Guid.Empty, productId, variantId);
    }

    private static AppException Invalid(string code, string message) {
        return new AppException(400, code, message);
    }

    private void RequireModule() {
        if (!tenant.HasModule(Module)) throw InventoryErrors.ModuleDisabled();
    }

    private static string Label(Product product) {
        return $"{product.Code} - {product.Name}";
    }

    // ---- şekil denetimi --------------------------------------------------------------------------------------

    public void ValidateInbound(Product product, decimal quantity, IReadOnlyList<LotInput>? lots, IReadOnlyList<string>? serials) {
        if (!IsTracked(product)) return;

        RequireModule();
        if (ModeOf(product) == TrackingMode.Lot) {
            if (lots is not { Count: > 0 }) throw Invalid("lot_required", $"{Label(product)} için lot bilgisi zorunludur.");
            if (lots.Any(l => string.IsNullOrWhiteSpace(l.LotNo) || l.LotNo.Trim().Length > 60 || l.Quantity <= 0m))
                throw Invalid("lot_required", $"{Label(product)}: lot numarası ve miktarı geçerli olmalıdır.");
            if (lots.Sum(l => l.Quantity) != quantity)
                throw Invalid("lot_quantity_mismatch", $"{Label(product)}: lot miktarlarının toplamı satır miktarına eşit olmalıdır.");
        } else {
            ValidateSerials(product, quantity, serials);
        }
    }

    public void ValidateOutbound(Product product, decimal quantity, IReadOnlyList<LotInput>? lots, IReadOnlyList<string>? serials) {
        if (!IsTracked(product)) return;

        RequireModule();
        if (ModeOf(product) == TrackingMode.Lot) {
            if (lots is not { Count: > 0 }) throw Invalid("lot_required", $"{Label(product)} için çıkış yapılacak lot seçilmelidir.");
            if (lots.Any(l => l.LotId is null || l.Quantity <= 0m))
                throw Invalid("lot_required", $"{Label(product)}: çıkış satırlarında lot seçimi ve miktar zorunludur.");
            if (lots.Sum(l => l.Quantity) != quantity)
                throw Invalid("lot_quantity_mismatch", $"{Label(product)}: lot miktarlarının toplamı satır miktarına eşit olmalıdır.");
        } else {
            ValidateSerials(product, quantity, serials);
        }
    }

    private static void ValidateSerials(Product product, decimal quantity, IReadOnlyList<string>? serials) {
        if (serials is not { Count: > 0 }) throw Invalid("serial_required", $"{Label(product)} için seri numaraları zorunludur.");
        if (serials.Any(s => string.IsNullOrWhiteSpace(s) || s.Trim().Length > 100))
            throw Invalid("serial_required", $"{Label(product)}: seri numarası boş olamaz ve en fazla 100 karakter olabilir.");
        if (serials.Select(s => s.Trim()).Distinct(StringComparer.Ordinal).Count() != serials.Count)
            throw Invalid("serial_duplicate", $"{Label(product)}: aynı seri numarası bir satırda birden fazla kez girilemez.");
        if (quantity != serials.Count)
            throw Invalid("serial_quantity_mismatch", $"{Label(product)}: seri numarası sayısı satır miktarına eşit olmalıdır.");
    }

    // ---- lot bakiyesi ----------------------------------------------------------------------------------------

    /// <summary>Lotların depo bazında bakiyesi: (depo, lot) -> miktar.</summary>
    public async Task<Dictionary<(Guid WarehouseId, Guid LotId), decimal>> LotBalancesAsync(Guid productId, Guid? variantId, Guid? warehouseId,
                                                                                            CancellationToken ct) {
        var movements = db.StockMovements.AsNoTracking().Where(m => m.ProductId == productId && m.VariantId == variantId);
        if (warehouseId is { } only) movements = movements.Where(m => m.WarehouseId == only);

        var rows = await movements.SelectMany(m => m.LotLines.Where(l => l.LotId != null).Select(l => new { m.WarehouseId, LotId = l.LotId!.Value, l.Quantity }))
                                  .ToListAsync(ct);
        return rows.GroupBy(r => (r.WarehouseId, r.LotId)).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
    }

    private async Task<decimal> LotAvailableAsync(Guid warehouseId, Guid productId, Guid? variantId, Guid lotId, CancellationToken ct) {
        return await db.StockMovements.Where(m => m.WarehouseId == warehouseId && m.ProductId == productId && m.VariantId == variantId)
                       .SelectMany(m => m.LotLines.Where(l => l.LotId == lotId))
                       .SumAsync(l => (decimal?)l.Quantity, ct) ?? 0m;
    }

    private static AppException LotInsufficient(Product product, string lot) {
        return new AppException(409, "lot_insufficient", $"{Label(product)}: \"{lot}\" lotunda yeterli stok yok.");
    }

    // ---- yazım ------------------------------------------------------------------------------------------------

    /// <summary>Giriş hareketine lot / seri satırlarını ekler; lotu yoksa oluşturur, seriyi depoya yerleştirir.</summary>
    public async Task ApplyInboundAsync(StockMovement movement, Product product, IReadOnlyList<LotInput>? lots, IReadOnlyList<string>? serials,
                                        CancellationToken ct) {
        if (!IsTracked(product)) return;

        if (ModeOf(product) == TrackingMode.Lot) {
            foreach (var group in lots!.GroupBy(l => l.LotNo.Trim(), StringComparer.Ordinal)) {
                var lot = await db.Lots.FirstOrDefaultAsync(l => l.ProductId == product.Id && l.VariantId == movement.VariantId
                                                                 && l.LotNo == group.Key, ct);
                var expiry = group.Select(g => g.Expiry).FirstOrDefault(e => e is not null);
                if (lot is null) {
                    lot = new Lot {
                        ProductId = product.Id, VariantId = movement.VariantId, LotNo = group.Key, ExpiryDate = expiry,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    db.Lots.Add(lot);
                } else if (lot.ExpiryDate is null && expiry is not null) {
                    lot.ExpiryDate = expiry;
                }

                movement.LotLines.Add(new MovementLotLine { LotId = lot.Id, Quantity = group.Sum(g => g.Quantity) });
            }
        } else {
            foreach (var number in serials!.Select(s => s.Trim())) await PlaceSerialAsync(movement, product, number, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Çıkış hareketine lot / seri satırlarını ekler; lot yetersizse ya da seri bu depoda değilse reddeder.</summary>
    public async Task ApplyOutboundAsync(StockMovement movement, Product product, IReadOnlyList<LotInput>? lots, IReadOnlyList<string>? serials,
                                         CancellationToken ct) {
        if (!IsTracked(product)) return;

        if (ModeOf(product) == TrackingMode.Lot) {
            foreach (var group in lots!.GroupBy(l => l.LotId!.Value)) {
                var lot = await db.Lots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == group.Key && l.ProductId == product.Id
                                                                                && l.VariantId == movement.VariantId, ct)
                          ?? throw LotInsufficient(product, group.Key.ToString());
                var quantity = group.Sum(g => g.Quantity);
                if (await LotAvailableAsync(movement.WarehouseId, product.Id, movement.VariantId, lot.Id, ct) < quantity)
                    throw LotInsufficient(product, lot.LotNo);

                movement.LotLines.Add(new MovementLotLine { LotId = lot.Id, Quantity = -quantity });
            }
        } else {
            foreach (var number in serials!.Select(s => s.Trim())) await RemoveSerialAsync(movement, product, number, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Transfer girişi: çıkıştaki lot / seri satırlarını hedef depoya yansıtır.</summary>
    public async Task ApplyTransferInAsync(StockMovement inbound, StockMovement outbound, Product product, CancellationToken ct) {
        if (!IsTracked(product)) return;

        foreach (var line in outbound.LotLines) {
            if (line.LotId is not null) {
                inbound.LotLines.Add(new MovementLotLine { LotId = line.LotId, Quantity = -line.Quantity });
            } else if (line.SerialId is { } serialId) {
                var serial = await db.SerialNumbers.FirstAsync(s => s.Id == serialId, ct);
                serial.WarehouseId = inbound.WarehouseId;
                inbound.LotLines.Add(new MovementLotLine { SerialId = serialId, Quantity = 1m });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>İptal hareketi: asıl hareketin lot / seri satırlarının tersi yazılır; seri konumu buna göre değişir.</summary>
    public async Task ApplyReversalAsync(StockMovement reversal, StockMovement original, Product product, CancellationToken ct) {
        foreach (var line in original.LotLines) {
            if (line.LotId is { } lotId) {
                // Çıkış iptali ise (yani asıl hareket girişse) lot bu depoda yeterli olmalı.
                if (reversal.Quantity < 0m && await LotAvailableAsync(reversal.WarehouseId, product.Id, reversal.VariantId, lotId, ct) < Math.Abs(line.Quantity)) {
                    var lot = await db.Lots.AsNoTracking().FirstAsync(l => l.Id == lotId, ct);
                    throw LotInsufficient(product, lot.LotNo);
                }

                reversal.LotLines.Add(new MovementLotLine { LotId = lotId, Quantity = -line.Quantity });
            } else if (line.SerialId is { } serialId) {
                var serial = await db.SerialNumbers.FirstAsync(s => s.Id == serialId, ct);
                if (reversal.Quantity > 0m) {
                    if (serial.WarehouseId is not null) throw SerialInStock(product, serial.Serial);

                    serial.WarehouseId = reversal.WarehouseId;
                } else {
                    if (serial.WarehouseId != reversal.WarehouseId) throw SerialNotInStock(product, serial.Serial);

                    serial.WarehouseId = null;
                }

                reversal.LotLines.Add(new MovementLotLine { SerialId = serialId, Quantity = -line.Quantity });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static AppException SerialInStock(Product product, string serial) {
        return new AppException(409, "serial_in_stock", $"{Label(product)}: \"{serial}\" seri numarası zaten stokta.");
    }

    private static AppException SerialNotInStock(Product product, string serial) {
        return new AppException(409, "serial_not_in_stock", $"{Label(product)}: \"{serial}\" seri numarası bu depoda stokta değil.");
    }

    private async Task PlaceSerialAsync(StockMovement movement, Product product, string number, CancellationToken ct) {
        var serial = await db.SerialNumbers.FirstOrDefaultAsync(s => s.ProductId == product.Id && s.Serial == number, ct);
        if (serial is null) {
            serial = new SerialNumber { ProductId = product.Id, VariantId = movement.VariantId, Serial = number, WarehouseId = movement.WarehouseId };
            db.SerialNumbers.Add(serial);
        } else {
            if (serial.WarehouseId is not null) throw SerialInStock(product, number);

            serial.VariantId   = movement.VariantId;
            serial.WarehouseId = movement.WarehouseId;
        }

        movement.LotLines.Add(new MovementLotLine { SerialId = serial.Id, Quantity = 1m });
    }

    private async Task RemoveSerialAsync(StockMovement movement, Product product, string number, CancellationToken ct) {
        var serial = await db.SerialNumbers.FirstOrDefaultAsync(s => s.ProductId == product.Id && s.Serial == number, ct);
        if (serial is null || serial.WarehouseId != movement.WarehouseId || serial.VariantId != movement.VariantId)
            throw SerialNotInStock(product, number);

        serial.WarehouseId = null;
        movement.LotLines.Add(new MovementLotLine { SerialId = serial.Id, Quantity = -1m });
    }
}
