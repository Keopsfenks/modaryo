using ERP.Application.Purchasing.Invoices;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing;
using ERP.Application.Pricing.Quotes;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;
using ERP.Domain.Invoicing;
using ERP.Domain.Pricing;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Orders;

public sealed class PoInputValidator : AbstractValidator<PoInput> {
    public PoInputValidator() {
        RuleFor(i => i.SupplierId).NotEmpty().WithMessage("Tedarikçi seçilmelidir.");
        RuleFor(i => i.BranchId).NotEmpty().WithMessage("Şube seçilmelidir.");
        RuleFor(i => i.Date).Must(d => d != default).WithMessage("Sipariş tarihi zorunludur.");
        RuleFor(i => i.Currency).Must(c => c is not null && InvoiceRules.Currencies.Contains(c.Trim().ToUpperInvariant()))
            .WithMessage("Para birimi TRY, USD, EUR ya da GBP olmalıdır.");
        RuleFor(i => i.Rate).GreaterThan(0m).LessThan(1_000_000m).When(i => i.Rate is not null).WithMessage("Kur sıfırdan büyük olmalıdır.");
        RuleFor(i => i.Notes).MaximumLength(1000).WithMessage("Not en fazla 1000 karakter olabilir.");
        RuleFor(i => i.Lines).NotEmpty().WithMessage("Siparişte en az bir satır olmalıdır.")
            .Must(l => l is null || l.Count <= 500).WithMessage("Siparişte en fazla 500 satır olabilir.");
        RuleForEach(i => i.Lines).ChildRules(l => {
            l.RuleFor(x => x.ProductId).NotEmpty().WithMessage("Kalem seçilmelidir.");
            l.RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0m).WithMessage("Miktar negatif olamaz.")
                .Must(q => Math.Round(q, 6) == q).WithMessage("Miktar en fazla 6 ondalık basamak içerebilir.");
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0m).When(x => x.UnitPrice is not null).WithMessage("Birim fiyat negatif olamaz.")
                .Must(p => p is null || Math.Round(p.Value, 6) == p).WithMessage("Birim fiyat en fazla 6 ondalık basamak içerebilir.");
            l.RuleFor(x => x.DiscountPercent).InclusiveBetween(0m, 100m).When(x => x.DiscountPercent is not null)
                .WithMessage("Satır iskonto yüzdesi 0 ile 100 arasında olmalıdır.");
            l.RuleFor(x => x.VatRate).Must(r => InvoiceCalculator.AllowedVatRates.Contains(r!.Value)).When(x => x.VatRate is not null)
                .WithMessage("KDV oranı 0, 1, 10 ya da 20 olmalıdır.");
        });
    }
}

/// <summary>Sipariş girdisini doğrular, fiyatları çözer, satır toplamlarını hesaplar ve siparişe yazar.</summary>
public sealed class PoBuilder(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    IBranchDirectory      branches,
    IPriceQuoteService    quotes,
    IExchangeRates        rates
) {
    public async Task ApplyAsync(PurchaseOrder po, PoInput input, CancellationToken ct) {
        // Fiyat girişi alış fiyatı yetkisi ister; yetkisiz kullanıcı fiyat görmeden sipariş yazamaz.
        if (!session.HasPermission(PurchasingPermissions.PurchasePrice)) throw new ForbiddenAccessException();

        BranchGuard.Ensure(tenant, input.BranchId);
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        if (!await branches.ExistsActiveAsync(tenantId, input.BranchId, ct))
            throw new AppException(404, "branch_not_found", "Şube bulunamadı ya da pasif.");

        var supplier = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.SupplierId, ct)
                       ?? throw new NotFoundException(input.SupplierId.ToString(), "CurrentAccount");
        if (!supplier.IsActive) throw PurchasingErrors.Conflict("current_account_inactive", "Cari pasif; sipariş verilemez.");
        if (!supplier.IsSupplier) throw PurchasingErrors.Bad("cari_not_supplier", "Satın alma siparişi için cari tedarikçi olmalıdır.");

        var currency = input.Currency.Trim().ToUpperInvariant();
        var rate     = await ResolveRateAsync(currency, input.Rate, input.Date, ct);

        var productIds = input.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Include(p => p.Units).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var variantIds = input.Lines.Where(l => l.VariantId is not null).Select(l => l.VariantId!.Value).Distinct().ToList();
        var variants   = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);

        var headerExpected = input.ExpectedDate ?? input.Date;
        var lines          = new List<PurchaseOrderLine>();
        var calcLines      = new List<CalcLine>();
        for (var n = 0; n < input.Lines.Count; n++) {
            var l     = input.Lines[n];
            var label = $"{n + 1}. satır";
            if (!products.TryGetValue(l.ProductId, out var product)) throw PurchasingErrors.Bad("product_not_found", $"{label}: kalem bulunamadı.");
            if (!product.IsActive) throw PurchasingErrors.Bad("product_inactive", $"{label}: kalem pasif.");
            if (l.VariantId is { } vid && (!variants.TryGetValue(vid, out var variant) || variant.ProductId != product.Id))
                throw PurchasingErrors.Bad("variant_invalid", $"{label}: varyant kaleme ait değil.");

            var (unit, factor)            = ResolveUnit(l, product, label);
            var (quantity, width, length) = ResolveQuantity(l, product, label);
            var vatRate                   = l.VatRate ?? product.VatRate;
            if (!InvoiceCalculator.AllowedVatRates.Contains(vatRate)) throw PurchasingErrors.Bad("invalid_vat_rate", $"{label}: KDV oranı geçersiz.");

            var price = l.UnitPrice ?? await QuoteAsync(supplier.Id, product.Id, l.VariantId, unit, quantity, vatRate, currency, input.Date, ct);
            lines.Add(new PurchaseOrderLine {
                Sort = n + 1, ProductId = product.Id, VariantId = l.VariantId, Description = product.Name, Unit = unit, Factor = factor,
                Quantity = quantity, Width = width, Length = length, UnitPrice = price, DiscountPercent = l.DiscountPercent, VatRate = vatRate,
                ExpectedDate = l.ExpectedDate ?? headerExpected, SalesOrderLineId = l.SalesOrderLineId
            });
            calcLines.Add(new CalcLine(quantity, price, false, l.DiscountPercent, null, vatRate, null, null, null, false));
        }

        CalcResult calc;
        try {
            calc = InvoiceCalculator.Calculate(calcLines, null, null);
        } catch (ArgumentException e) {
            throw PurchasingErrors.Bad("invalid_amounts", e.Message);
        }

        for (var i = 0; i < lines.Count; i++) {
            lines[i].Net   = calc.Lines[i].Net;
            lines[i].Vat   = calc.Lines[i].Vat;
            lines[i].Total = calc.Lines[i].Total;
        }

        po.SupplierId   = supplier.Id;
        po.BranchId     = input.BranchId;
        po.Date         = input.Date;
        po.ExpectedDate = input.ExpectedDate;
        po.Currency     = currency;
        po.Rate         = rate;
        po.Notes        = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        po.Gross        = calc.Gross;
        po.Discount     = calc.Discount;
        po.Net          = calc.Net;
        po.Vat          = calc.Vat;
        po.Total        = calc.Payable;

        foreach (var line in lines) {
            line.PurchaseOrderId = po.Id;
            po.Lines.Add(line);
        }
    }

    /// <summary>Sipariş kuru yalnızca bilgidir; kur bulunamazsa 1 yazılır (geçerli kur mal kabulde alınır).</summary>
    private async Task<decimal> ResolveRateAsync(string currency, decimal? given, DateOnly date, CancellationToken ct) {
        if (currency == "TRY") return 1m;
        if (given is { } manual) return manual;
        try {
            return (await rates.GetAsync(currency, date, RateType.ForexBuying, ct)).Rate;
        } catch (AppException) {
            return 1m;
        }
    }

    /// <summary>Alış önceliği: tedarikçi listesi, varsayılan liste, kart. Para birimi siparişinkiyle aynı değilse fiyat önerilmez.</summary>
    private async Task<decimal> QuoteAsync(Guid supplierId, Guid productId, Guid? variantId, string unit, decimal quantity, int vatRate,
                                           string currency, DateOnly date, CancellationToken ct) {
        var quote = await quotes.QuoteAsync(new QuoteRequest(PriceListKind.Purchase, supplierId, productId, variantId, unit, quantity, date), ct);
        if (quote is null || !string.Equals(quote.Currency, currency, StringComparison.OrdinalIgnoreCase)) return 0m;
        return quote.VatIncluded ? Math.Round(quote.UnitPrice / (1m + vatRate / 100m), 6, MidpointRounding.AwayFromZero) : quote.UnitPrice;
    }

    private static (string Unit, decimal Factor) ResolveUnit(PoLineInput l, Product product, string label) {
        var requested = l.Unit?.Trim();
        if (string.IsNullOrEmpty(requested) || string.Equals(requested, product.BaseUnit, StringComparison.OrdinalIgnoreCase)) return (product.BaseUnit, 1m);
        var unit = product.Units.FirstOrDefault(u => string.Equals(u.Unit, requested, StringComparison.OrdinalIgnoreCase))
                   ?? throw PurchasingErrors.Bad("unit_invalid", $"{label}: \"{requested}\" birimi kalemde tanımlı değil.");
        return (unit.Unit, unit.Factor);
    }

    /// <summary>Ölçüyle alınan kalemlerde miktar en x boy (m2) ya da boydur (m).</summary>
    private static (decimal Quantity, decimal? Width, decimal? Length) ResolveQuantity(PoLineInput l, Product product, string label) {
        if (!product.SoldByMeasure) {
            if (l.Quantity <= 0m) throw PurchasingErrors.Bad("quantity_required", $"{label}: miktar sıfırdan büyük olmalıdır.");
            return (l.Quantity, null, null);
        }

        var byLength = string.Equals(product.MeasureUnit, "m", StringComparison.OrdinalIgnoreCase);
        if (l.Length is not > 0m || (!byLength && l.Width is not > 0m))
            throw PurchasingErrors.Bad("measure_dimensions_required", byLength ? $"{label}: boy zorunludur." : $"{label}: en ve boy zorunludur.");
        var quantity = byLength ? l.Length.Value : Math.Round(l.Width!.Value * l.Length.Value, 6, MidpointRounding.AwayFromZero);
        return (quantity, byLength ? null : l.Width, l.Length);
    }
}

public static class PoRules {
    public static bool IsOverdue(PurchaseOrder po, DateOnly today) {
        return po.Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived
               && po.Lines.Any(l => l.OpenQuantity > 0m && (l.ExpectedDate ?? po.ExpectedDate ?? po.Date) < today);
    }

    /// <summary>Taslak olmayan sipariş değiştirilemez.</summary>
    public static void EnsureDraft(PurchaseOrder po) {
        if (po.Status != PurchaseOrderStatus.Draft) throw PurchasingErrors.PoNotDraft();
    }

    public static async Task<PurchaseOrder> LoadAsync(IApplicationDbContext db, ICurrentTenant tenant, Guid id, bool track, CancellationToken ct) {
        var query = db.PurchaseOrders.Include(p => p.Lines).AsQueryable();
        if (!track) query = query.AsNoTracking();
        var po = await query.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException(id.ToString(), "PurchaseOrder");
        BranchGuard.Ensure(tenant, po.BranchId);
        return po;
    }
}

/// <summary>Siparişi ayrıntı DTO'suna çevirir: kalem adları, son alış ipucu, üçlü karşılaştırma, mal kabuller ve faturalar.</summary>
public sealed class PoMapper(
    IApplicationDbContext db,
    ICurrentSession       session,
    ITenantSettings       settings
) {
    public async Task<PoDto> ToDtoAsync(PurchaseOrder po, CancellationToken ct) {
        var showPrices = session.HasPermission(PurchasingPermissions.PurchasePrice);
        var supplier   = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == po.SupplierId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var productIds = po.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);

        var hints = showPrices ? await LastPurchasesAsync(po.Lines, ct) : new Dictionary<(Guid, Guid?), LastPurchaseDto>();
        var lines = po.Lines.OrderBy(l => l.Sort).Select(l => {
            var hint = hints.GetValueOrDefault((l.ProductId, l.VariantId));
            var info = products.GetValueOrDefault(l.ProductId);
            return new PoLineDto(l.Id, l.Sort, l.ProductId, info?.Code ?? string.Empty, info?.Name ?? l.Description, l.VariantId, l.Description, l.Unit, l.Factor,
                                 l.Quantity, l.ReceivedQuantity, l.ClosedQuantity, l.OpenQuantity, l.Width, l.Length, showPrices ? l.UnitPrice : null,
                                 showPrices ? l.DiscountPercent : null, l.VatRate, l.ExpectedDate, l.SalesOrderLineId, showPrices ? l.Net : null,
                                 showPrices ? l.Vat : null, showPrices ? l.Total : null,
                                 hint is null ? null : new LastPurchaseDto(Math.Round(hint.UnitPriceTl * l.Factor, 6, MidpointRounding.AwayFromZero), hint.Date));
        }).ToList();

        var receipts = await db.GoodsReceipts.AsNoTracking().Where(r => r.PurchaseOrderId == po.Id).OrderBy(r => r.Date).ThenBy(r => r.Number)
                               .Select(r => new PoReceiptRefDto(r.Id, r.Number, r.Date, r.Status)).ToListAsync(ct);
        // Fatura bağı mal kabul üzerindendir: fatura kaynağı "purchase-receipt"; sipariş, mal kabuller üzerinden bulunur.
        var receiptIds = receipts.Select(r => r.Id).ToList();
        var invoices   = (await ReceiptInvoiceLinks.InvoicesAsync(db, receiptIds, ct)).Select(i => new { i.Id, i.Number, i.Date, i.Status }).ToList();
        var matchRows = await MatchRowsAsync(po, showPrices, ct);

        return new PoDto(po.Id, po.Number, po.Status, po.SupplierId, supplier, po.BranchId, po.Date, po.ExpectedDate, po.Currency, po.Rate, po.Notes,
                         po.CancelReason, po.CloseReason, showPrices ? po.Gross : null, showPrices ? po.Discount : null, showPrices ? po.Net : null,
                         showPrices ? po.Vat : null, showPrices ? po.Total : null, po.Version, lines, matchRows, receipts,
                         invoices.Select(i => new PoInvoiceRefDto(i.Id, i.Number, i.Date, i.Status.ToString())).ToList());
    }

    /// <summary>
    ///     Satır başına sipariş / gelen / faturalanan miktar ve fiyatlar. Fiyat karşılaştırması yalnızca aynı para birimindeki
    ///     mal kabul ve fatura satırları arasında yapılır.
    /// </summary>
    private async Task<IReadOnlyList<MatchRowDto>> MatchRowsAsync(PurchaseOrder po, bool showPrices, CancellationToken ct) {
        var variance    = (await settings.GetAsync<PurchasingSettings>(ct)).PriceVariancePercent;
        var lineIds     = po.Lines.Select(l => l.Id).ToList();
        var receiptRows = await db.GoodsReceiptLines.AsNoTracking()
                              .Where(l => l.PurchaseOrderLineId != null && lineIds.Contains(l.PurchaseOrderLineId.Value))
                              .Join(db.GoodsReceipts.Where(r => r.Status == GoodsReceiptStatus.Posted && r.Currency == po.Currency), l => l.GoodsReceiptId, r => r.Id,
                                    (l, _) => new { Line = l.PurchaseOrderLineId!.Value, l.Quantity, l.BaseQuantity, l.UnitPrice }).ToListAsync(ct);
        var receiptIds  = await db.GoodsReceipts.AsNoTracking().Where(r => r.PurchaseOrderId == po.Id && r.Status == GoodsReceiptStatus.Posted).Select(r => r.Id).ToListAsync(ct);
        var linkedLines = new List<(Guid PoLine, InvoiceLine Line, string Currency)>();
        foreach (var invoice in (await ReceiptInvoiceLinks.InvoicesAsync(db, receiptIds, ct)).Where(i => i.Status == InvoiceStatus.Issued))
            foreach (var link in await ReceiptInvoiceLinks.ResolveAsync(db, invoice, false, ct))
                if (link.ReceiptLine.PurchaseOrderLineId is { } poLineId && lineIds.Contains(poLineId))
                    linkedLines.Add((poLineId, link.Line, invoice.Currency));

        var rows = new List<MatchRowDto>();
        foreach (var line in po.Lines.OrderBy(l => l.Sort)) {
            var received = receiptRows.Where(r => r.Line == line.Id).ToList();
            var invoiced    = linkedLines.Where(r => r.PoLine == line.Id).ToList();
            var invoicedQty = invoiced.Sum(r => r.Line.Quantity * r.Line.Factor) / line.Factor;
            // Mal kabul satırı fiyatı kendi birimindedir (toplam tutar = Quantity x UnitPrice); temel birim üzerinden ortalama alınıp sipariş birimine çevrilir.
            var priced       = received.Where(r => r.UnitPrice is not null && r.BaseQuantity > 0m).ToList();
            var receiptBase  = priced.Sum(r => r.BaseQuantity);
            decimal? receiptPrice = receiptBase > 0m ? priced.Sum(r => r.Quantity * r.UnitPrice!.Value) / receiptBase * line.Factor : null;
            // Fiyat karşılaştırması yalnızca sipariş para biriminde kesilmiş faturalar arasında, iskonto sonrası net fiyatla yapılır.
            var sameCurrency = invoiced.Where(r => r.Currency == po.Currency).ToList();
            var invoiceBase  = sameCurrency.Sum(r => r.Line.Quantity * r.Line.Factor);
            decimal? invoicePrice = invoiceBase > 0m ? sameCurrency.Sum(r => r.Line.Net) / invoiceBase * line.Factor : null;

            var row = ThreeWayMatch.Evaluate(line.Id, line.Quantity, line.ReceivedQuantity, invoicedQty, line.UnitPrice, receiptPrice, invoicePrice, variance);
            rows.Add(new MatchRowDto(row.PoLineId, row.Ordered, row.Received, row.Invoiced, showPrices ? row.PoPrice : null,
                                     showPrices ? row.ReceiptPrice : null, showPrices ? row.InvoicePrice : null, row.QtyMismatch, showPrices && row.PriceVariance));
        }

        return rows;
    }

    /// <summary>Kalem (ve varyant) başına en son kesilmiş alış faturası satırının temel birim başına TL fiyatı.</summary>
    private async Task<Dictionary<(Guid, Guid?), LastPurchaseDto>> LastPurchasesAsync(IEnumerable<PurchaseOrderLine> lines, CancellationToken ct) {
        var result = new Dictionary<(Guid, Guid?), LastPurchaseDto>();
        foreach (var key in lines.Select(l => (l.ProductId, l.VariantId)).Distinct()) {
            var (productId, variantId) = key;
            var last = await db.InvoiceLines.AsNoTracking().Where(l => l.ProductId == productId && l.VariantId == variantId && l.Quantity > 0m)
                               .Join(db.Invoices.Where(i => i.Kind == InvoiceKind.Purchase && i.Status == InvoiceStatus.Issued), l => l.InvoiceId, i => i.Id,
                                     (l, i) => new { i.Date, i.Created, l.NetTl, l.Quantity, l.Factor })
                               .OrderByDescending(x => x.Date).ThenByDescending(x => x.Created).FirstOrDefaultAsync(ct);
            if (last is not null)
                result[key] = new LastPurchaseDto(Math.Round(last.NetTl / (last.Quantity * last.Factor), 6, MidpointRounding.AwayFromZero), last.Date);
        }

        return result;
    }
}
