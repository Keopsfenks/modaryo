using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Documents;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Inventory.Tracking;
using ERP.Application.Purchasing.Orders;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Inventory;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Receipts;

/// <summary>
///     Mal kabul. Sıra: açık işlem, sipariş kilidi (varsa), siparişin yeniden okunması, TÜM stok kilitleri tek seferde
///     (giriş deposu x kalem, takipli kalemlerde kalem kilidi, bekleyen rezervasyonların depoları), sonra stok girişi, rezervasyon
///     taşıma, sipariş güncelleme ve outbox. Gelen miktar tolerans denetimi sipariş kilidi altında yapılır.
/// </summary>
[Authorize(Permission = PurchasingPermissions.Receive)]
public sealed record CreateReceiptCommand(ReceiptInput Input) : IRequest<ReceiptDto>;

public sealed class CreateReceiptCommandValidator : AbstractValidator<CreateReceiptCommand> {
    public CreateReceiptCommandValidator() {
        RuleFor(c => c.Input).NotNull().SetValidator(new ReceiptInputValidator());
    }
}

public sealed class CreateReceiptCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    IStockService         stock,
    LineResolver          resolver,
    IExchangeRates        rates,
    ITenantSettings       settings,
    IOutbox               outbox,
    IPurchaseLinkListener links,
    ReceiptMapper         mapper
) : IRequestHandler<CreateReceiptCommand, ReceiptDto> {
    public async Task<ReceiptDto> Handle(CreateReceiptCommand request, CancellationToken ct) {
        var input     = request.Input;
        var canPrice = session.HasPermission(PurchasingPermissions.PurchasePrice);
        var warnings  = new List<string>();
        BranchGuard.Ensure(tenant, input.BranchId);

        await using var tx = await db.BeginTransactionAsync(ct);

        // 1) Sipariş kilidi, sonra siparişin (kilit altında) okunması.
        PurchaseOrder? po = null;
        if (input.PurchaseOrderId is { } poId) {
            if (!await db.LockPurchaseOrderAsync(poId, ct)) throw new NotFoundException(poId.ToString(), "PurchaseOrder");
            po = await PoRules.LoadAsync(db, tenant, poId, true, ct);
            EnsureReceivable(po);
            if (input.SupplierId is { } given && given != po.SupplierId)
                throw PurchasingErrors.Bad("supplier_mismatch", "Tedarikçi siparişin tedarikçisiyle aynı olmalıdır.");
            if (input.BranchId != po.BranchId) throw PurchasingErrors.Bad("branch_mismatch", "Şube siparişin şubesiyle aynı olmalıdır.");
        } else if (input.SupplierId is null) {
            throw PurchasingErrors.Bad("supplier_required", "Siparişsiz mal kabulde tedarikçi seçilmelidir.");
        }

        var supplierId = po?.SupplierId ?? input.SupplierId!.Value;
        var supplier   = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == supplierId, ct) ?? throw new NotFoundException(supplierId.ToString(), "CurrentAccount");
        if (!supplier.IsSupplier) throw PurchasingErrors.Bad("cari_not_supplier", "Mal kabul için cari tedarikçi olmalıdır.");

        // 2) Depo ve karantina yönlendirmesi.
        var purchasing = await settings.GetAsync<PurchasingSettings>(ct);
        var target     = await LoadWarehouseAsync(input.WarehouseId, input.BranchId, ct);
        var entry      = target;
        var held       = false;
        if (purchasing.QuarantineEnabled) {
            if (target.Kind != WarehouseKind.Normal) throw PurchasingErrors.Bad("warehouse_not_normal", "Karantina açıkken hedef depo normal bir depo olmalıdır.");
            if (purchasing.QuarantineWarehouseByBranch is null || !purchasing.QuarantineWarehouseByBranch.TryGetValue(input.BranchId, out var quarantineId))
                throw PurchasingErrors.Conflict("quarantine_warehouse_missing", "Bu şube için karantina deposu tanımlı değil.");
            entry = await LoadWarehouseAsync(quarantineId, input.BranchId, ct);
            if (entry.Kind != WarehouseKind.Quarantine) throw PurchasingErrors.Conflict("quarantine_warehouse_missing", "Tanımlı karantina deposu karantina türünde değil.");
            held = true;
        }

        // 3) Satırlar: ürün, birim, maliyet, tolerans.
        var currency = po?.Currency ?? (input.Currency ?? "TRY").Trim().ToUpperInvariant();
        var rate     = 1m;
        if (currency != "TRY") {
            var type = (await settings.GetAsync<FinanceSettings>(ct)).RateType;
            rate = (await rates.GetAsync(currency, input.Date, type, ct)).Rate;
        }

        var products = await resolver.LoadProductsAsync(input.Lines.Select(l => l.ProductId), ct);
        var receipt  = new GoodsReceipt {
            Status = GoodsReceiptStatus.Posted, PurchaseOrderId = po?.Id, SupplierId = supplierId, BranchId = input.BranchId, WarehouseId = target.Id,
            Date = input.Date, Currency = currency, Rate = rate,
            SupplierDeliveryNo = string.IsNullOrWhiteSpace(input.DeliveryNoteNo) ? null : input.DeliveryNoteNo.Trim(),
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim()
        };

        var stockLines = new List<StockLine>();
        var poReceived = new Dictionary<Guid, decimal>(); // sipariş satırı -> bu mal kabulün (sipariş birimi) miktarı
        var sort       = 0;
        foreach (var l in input.Lines) {
            sort++;
            var label   = $"{sort}. satır";
            var product = products[l.ProductId];
            PurchaseOrderLine? poLine = null;
            if (po is not null) {
                if (l.PoLineId is not { } lineId) throw PurchasingErrors.Bad("po_line_required", $"{label}: siparişli mal kabulde sipariş satırı seçilmelidir.");
                poLine = po.Lines.FirstOrDefault(x => x.Id == lineId) ?? throw PurchasingErrors.Bad("po_line_invalid", $"{label}: sipariş satırı bu siparişe ait değil.");
                if (poLine.ProductId != l.ProductId || poLine.VariantId != l.VariantId)
                    throw PurchasingErrors.Bad("po_line_mismatch", $"{label}: kalem sipariş satırıyla uyuşmuyor.");
            } else if (l.PoLineId is not null) {
                throw PurchasingErrors.Bad("po_line_unexpected", $"{label}: siparişsiz mal kabulde sipariş satırı verilemez.");
            }

            // Fiyat: siparişli kabulde sipariş fiyatı (iskonto sonrası), serbest kabulde girilen fiyat (fiyat yetkisi ister).
            decimal? pricePerBase;
            if (poLine is not null) {
                var net = poLine.UnitPrice * (1m - (poLine.DiscountPercent ?? 0m) / 100m);
                pricePerBase = net / poLine.Factor;
            } else if (l.UnitPrice is not null) {
                if (!canPrice) throw new ForbiddenAccessException();
                pricePerBase = null; // aşağıda birime göre çözülür
            } else {
                pricePerBase = null;
            }

            var unit     = string.IsNullOrWhiteSpace(l.Unit) ? (poLine?.Unit ?? product.BaseUnit) : l.Unit.Trim();
            var resolved = LineResolver.Resolve(new LineInput(l.ProductId, l.VariantId, unit, l.Quantity, poLine is null ? l.UnitPrice : null, l.Lots, l.Serials), product);
            var factor   = resolved.BaseQuantity / l.Quantity;
            decimal? baseCostCurrency = poLine is not null ? pricePerBase : resolved.BaseUnitCost;
            decimal? unitCostTl       = baseCostCurrency is { } c ? Math.Round(c * rate, 6, MidpointRounding.AwayFromZero) : null;
            decimal? linePrice        = baseCostCurrency is { } b ? Math.Round(b * factor, 6, MidpointRounding.AwayFromZero) : null;

            if (poLine is not null) {
                var poQty = Math.Round(resolved.BaseQuantity / poLine.Factor, 6, MidpointRounding.AwayFromZero);
                poReceived[poLine.Id] = poReceived.GetValueOrDefault(poLine.Id) + poQty;
            }

            var lineEntity = new GoodsReceiptLine {
                GoodsReceiptId = receipt.Id, PurchaseOrderLineId = poLine?.Id, Sort = sort, ProductId = l.ProductId, VariantId = l.VariantId, Unit = resolved.Unit,
                Quantity = l.Quantity, BaseQuantity = resolved.BaseQuantity, UnitPrice = linePrice, UnitCostTl = unitCostTl, WarehouseId = entry.Id,
                Quarantine = held ? QuarantineState.Held : QuarantineState.None, TargetWarehouseId = held ? target.Id : null
            };
            receipt.Lines.Add(lineEntity);
            stockLines.Add(new StockLine(entry.Id, l.ProductId, l.VariantId, resolved.BaseQuantity, unitCostTl, resolved.Lots, resolved.Serials, lineEntity.Id));
        }

        if (po is not null) CheckTolerance(po, poReceived, purchasing.OverDeliveryPercent, warnings);

        // 4) TÜM stok kilitleri tek seferde: giriş anahtarları, takipli kalem kilitleri, bekleyen rezervasyonların depoları.
        var keys = stockLines.Select(s => (s.WarehouseId, s.ProductId, s.VariantId)).ToList();
        keys.AddRange(stockLines.Where(s => TrackingValidator.IsTracked(products[s.ProductId])).Select(s => TrackingValidator.ItemKey(s.ProductId)));
        var expected = new List<(Guid Warehouse, Guid Product, Guid? Variant)>();
        if (po is not null && !held) {
            var productIds = stockLines.Select(s => s.ProductId).Distinct().ToList();
            expected = (await db.Reservations.AsNoTracking()
                                .Where(r => r.ExpectedReceiptSourceId == po.Id && r.Status == ReservationStatus.Active && productIds.Contains(r.ProductId))
                                .Select(r => new { r.WarehouseId, r.ProductId, r.VariantId }).Distinct().ToListAsync(ct))
                .Select(r => (r.WarehouseId, r.ProductId, r.VariantId)).ToList();
            keys.AddRange(expected);
        }

        await db.LockStockAsync(keys, ct);

        // 5) Stok girişi ve rezervasyon taşıma.
        await stock.ReceiveAsync(MovementType.PurchaseReceipt, ReceiptRules.StockSource, receipt.Id, receipt.Date, stockLines, ct);
        if (expected.Count > 0)
            foreach (var group in stockLines.GroupBy(s => (s.ProductId, s.VariantId)))
                await stock.RetargetExpectedAsync(po!.Id, entry.Id, group.Sum(s => s.BaseQuantity), ct, group.Key.ProductId, group.Key.VariantId);

        // 6) Sipariş satırları ve durum.
        if (po is not null) {
            foreach (var (lineId, qty) in poReceived) po.Lines.First(x => x.Id == lineId).ReceivedQuantity += qty;
            ReceiptRules.RecomputeStatus(po);
        }

        receipt.Number = await DocumentNumbers.NextAsync(db, "MK", receipt.Date, ct);
        db.GoodsReceipts.Add(receipt);
        if (po is not null && !held)
            foreach (var l in receipt.Lines.Where(x => x.PurchaseOrderLineId is not null))
                await links.OnReceivedAsync(l.PurchaseOrderLineId!.Value, l.Quantity, receipt.SupplierDeliveryNo, ct);
        outbox.Enqueue("purchase.received", new {
            poId = po?.Id, receiptId = receipt.Id, number = receipt.Number, warehouseId = entry.Id, quarantined = held,
            lines = receipt.Lines.Select(x => {
                var poLine = x.PurchaseOrderLineId is { } id ? po!.Lines.First(p => p.Id == id) : null;
                return new { poLineId = x.PurchaseOrderLineId, salesOrderLineId = poLine?.SalesOrderLineId, productId = x.ProductId, variantId = x.VariantId, quantity = x.Quantity, baseQuantity = x.BaseQuantity, unit = x.Unit };
            }).ToList()
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(receipt, warnings, ct);
    }

    private static void EnsureReceivable(PurchaseOrder po) {
        switch (po.Status) {
            case PurchaseOrderStatus.Draft:     throw PurchasingErrors.Conflict("po_not_approved", "Sipariş henüz onaylanmadı; mal kabul yapılamaz.");
            case PurchaseOrderStatus.Cancelled: throw PurchasingErrors.Conflict("po_cancelled", "Sipariş iptal edilmiş; mal kabul yapılamaz.");
            case PurchaseOrderStatus.Closed:    throw PurchasingErrors.ReceiptExceedsOpen();
        }
    }

    /// <summary>
    ///     Kısa kapatılmış satırın açık miktarını aşan kabul reddedilir (receipt_exceeds_open). Aksi halde toplam gelen miktar
    ///     sipariş x (1 + tolerans) sınırını aşarsa 409 over_delivery; sınır içindeki fazlalık uyarı üretir.
    /// </summary>
    private static void CheckTolerance(PurchaseOrder po, Dictionary<Guid, decimal> incoming, decimal percent, List<string> warnings) {
        foreach (var (lineId, qty) in incoming) {
            var line = po.Lines.First(l => l.Id == lineId);
            if (qty <= line.OpenQuantity) continue;
            if (line.ClosedQuantity > 0m) throw PurchasingErrors.ReceiptExceedsOpen();

            var limit = line.Quantity * (1m + percent / 100m);
            if (line.ReceivedQuantity + qty > limit) throw PurchasingErrors.OverDelivery();
            if (!warnings.Contains("over_delivery_within_tolerance")) warnings.Add("over_delivery_within_tolerance");
        }
    }

    private async Task<Warehouse> LoadWarehouseAsync(Guid id, Guid branchId, CancellationToken ct) {
        var warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException(id.ToString(), "Warehouse");
        BranchGuard.Ensure(tenant, warehouse.BranchId);
        if (!warehouse.IsActive) throw PurchasingErrors.Conflict("warehouse_inactive", "Depo pasif.");
        if (warehouse.BranchId != branchId) throw PurchasingErrors.Bad("warehouse_branch_mismatch", "Depo, mal kabulün şubesine ait değil.");
        return warehouse;
    }
}

[Authorize(Permission = PurchasingPermissions.Receive)]
public sealed record CancelReceiptCommand(Guid Id, string Reason) : IRequest<ReceiptDto>;

public sealed class CancelReceiptCommandValidator : AbstractValidator<CancelReceiptCommand> {
    public CancelReceiptCommandValidator() {
        RuleFor(c => c.Reason).NotEmpty().WithMessage("İptal nedeni zorunludur.")
            .Length(2, 200).WithMessage("İptal nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

/// <summary>
///     Mal kabul iptali: faturalanmış ya da karantina/iade işlemi görmüş mal kabul iptal edilemez. Sıra: sipariş kilidi, yeniden okuma,
///     stok kilitleri, stok iptali, sipariş satırları.
/// </summary>
public sealed class CancelReceiptCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    IOutbox               outbox,
    ReceiptMapper         mapper
) : IRequestHandler<CancelReceiptCommand, ReceiptDto> {
    public async Task<ReceiptDto> Handle(CancelReceiptCommand request, CancellationToken ct) {
        var head = await ReceiptRules.LoadAsync(db, tenant, request.Id, false, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockReceiptsAsync([head.Id], ct); // faturalama kilidi: eşzamanlı fatura taslağı ile iptal sıraya girer
        PurchaseOrder? po = null;
        if (head.PurchaseOrderId is { } poId) {
            await db.LockPurchaseOrderAsync(poId, ct);
            po = await PoRules.LoadAsync(db, tenant, poId, true, ct);
        }

        var receipt = await ReceiptRules.LoadAsync(db, tenant, request.Id, true, ct);
        if (receipt.Status == GoodsReceiptStatus.Cancelled) throw PurchasingErrors.Conflict("receipt_cancelled", "Mal kabul zaten iptal edilmiş.");
        if (await ReceiptRules.IsInvoicedAsync(db, receipt.Id, ct))
            throw PurchasingErrors.Conflict("receipt_invoiced", "Faturalanmış mal kabul iptal edilemez; önce faturayı iptal edin.");

        var lineIds = receipt.Lines.Select(l => l.Id).ToList();
        if (await db.StockMovements.AnyAsync(m => m.SourceLineId != null && lineIds.Contains(m.SourceLineId.Value) && m.SourceType != ReceiptRules.StockSource, ct))
            throw PurchasingErrors.Conflict("receipt_processed", "Karantina işlemi ya da iadesi yapılmış mal kabul iptal edilemez.");
        if (await db.LandedCostTargets.AnyAsync(t => t.GoodsReceiptId == receipt.Id && db.LandedCosts.Any(c => c.Id == t.LandedCostId && c.Status == LandedCostStatus.Posted), ct))
            throw PurchasingErrors.Conflict("receipt_has_landed_cost", "İşlenmiş masraf dağıtımı olan mal kabul iptal edilemez; önce masraf dağıtımını iptal edin.");
        if (await HasNetCostCorrectionAsync(receipt.Id, ct))
            throw PurchasingErrors.Conflict("receipt_processed", "Maliyet düzeltmesi yapılmış mal kabul iptal edilemez.");

        var keys = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == ReceiptRules.StockSource && m.SourceId == receipt.Id && m.Quantity != 0m)
                           .Select(m => new { m.WarehouseId, m.ProductId, m.VariantId, Tracked = m.LotLines.Any() }).Distinct().ToListAsync(ct);
        if (keys.Count > 0)
            await db.LockStockAsync(keys.Select(k => (k.WarehouseId, k.ProductId, k.VariantId))
                                        .Concat(keys.Where(k => k.Tracked).Select(k => TrackingValidator.ItemKey(k.ProductId))), ct);
        await stock.ReverseAsync(ReceiptRules.StockSource, receipt.Id, receipt.Date, ct);

        receipt.Status       = GoodsReceiptStatus.Cancelled;
        receipt.CancelReason = request.Reason.Trim();
        if (po is not null) {
            foreach (var line in receipt.Lines.Where(l => l.PurchaseOrderLineId is not null)) {
                var poLine = po.Lines.First(p => p.Id == line.PurchaseOrderLineId);
                poLine.ReceivedQuantity = Math.Max(0m, poLine.ReceivedQuantity - Math.Round(line.BaseQuantity / poLine.Factor, 6, MidpointRounding.AwayFromZero));
            }

            ReceiptRules.RecomputeStatus(po);
        }

        outbox.Enqueue("purchase.receipt-cancelled", new { poId = receipt.PurchaseOrderId, receiptId = receipt.Id, number = receipt.Number, reason = receipt.CancelReason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(receipt, [], ct);
    }

    /// <summary>Girişlerine yazılan maliyet düzeltmelerinin (Revaluation, SourceLineId yok) neti sıfırdan farklıysa true; iptal edilmiş düzeltmeler netleşir.</summary>
    private async Task<bool> HasNetCostCorrectionAsync(Guid receiptId, CancellationToken ct) {
        var inbound = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == ReceiptRules.StockSource && m.SourceId == receiptId && m.Quantity > 0m)
                              .Select(m => m.Id).ToListAsync(ct);
        if (inbound.Count == 0) return false;

        var notes = inbound.Select(id => "correction:" + id).ToList();
        var nets  = await db.StockMovements.AsNoTracking().Where(m => m.Type == MovementType.Revaluation && m.Note != null && notes.Contains(m.Note))
                            .GroupBy(m => m.Note).Select(g => g.Sum(m => m.TotalCost)).ToListAsync(ct);
        return nets.Any(n => n != 0m);
    }
}
