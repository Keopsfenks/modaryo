using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory.Tracking;
using ERP.Application.Pricing;
using ERP.Application.Pricing.Campaigns;
using ERP.Application.Pricing.Quotes;
using ERP.Domain.Catalog;
using ERP.Domain.Invoicing;
using ERP.Domain.Pricing;

namespace ERP.Application.Pos.Sales;

/// <summary>
///     Çözülmüş satış satırı. Tutarlar TL'dir ve KDV hariçtir: UnitPrice (6 hane) hariç birim fiyat, Discount elle iskonto, CampaignDiscount otomatik kampanya,
///     Net = brüt - iskontolar, Vat = net x oran, Total = Net + Vat. Terazi fiyat tipinde (FixedTotal) Total barkoddaki tutara eşittir.
/// </summary>
public sealed record ResolvedSaleLine(
    int      Sort,
    Product  Product,
    Guid?    VariantId,
    string   Description,
    string   Unit,
    decimal  Factor,
    decimal  Quantity,
    decimal  UnitPrice,
    int      VatRate,
    decimal  Discount,
    decimal  CampaignDiscount,
    Guid?    CampaignId,
    decimal  Net,
    decimal  Vat,
    decimal  Total
) {
    public decimal BaseQuantity => Math.Round(Quantity * Factor, 6, MidpointRounding.AwayFromZero);
}

public sealed record ResolvedSale(IReadOnlyList<ResolvedSaleLine> Lines) {
    public decimal Subtotal => Lines.Sum(l => l.Net);
    public decimal Vat      => Lines.Sum(l => l.Vat);
    public decimal Total    => Lines.Sum(l => l.Total);
    public decimal Discount => Lines.Sum(l => l.Discount + l.CampaignDiscount);
}

/// <summary>
///     Satış satırlarını sunucuda çözer: kalem, birim, fiyat (P2 satış önceliği, müşteriye göre), otomatik kampanya (satır başına en çok biri) ve elle iskonto
///     (iskonto politikası yalnızca elle iskontoya uygulanır). Kilit almaz; satır hesabı yuvarlamada fatura hesaplayıcısıyla birebir aynıdır
///     (e-Arşiv faturası aynı tutarlara çıkar).
/// </summary>
public sealed class PosSaleResolver(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IPriceQuoteService    quotes,
    ICampaignService      campaigns,
    TimeProvider          time
) {
    public async Task<ResolvedSale> ResolveAsync(IReadOnlyList<SaleLineInput> input, Guid customerId, DateOnly date, CancellationToken ct) {
        if (input.Count == 0) throw PosErrors.Bad("sale_empty", "Satışta en az bir satır olmalıdır.");
        if (input.Count > 300) throw PosErrors.Bad("sale_too_large", "Bir satışta en fazla 300 satır olabilir.");

        var productIds = input.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Include(p => p.Units).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var variantIds = input.Where(l => l.VariantId is not null).Select(l => l.VariantId!.Value).Distinct().ToList();
        var variants   = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);

        var prepared = new List<Prepared>();
        for (var n = 0; n < input.Count; n++) {
            var l     = input[n];
            var label = $"{n + 1}. satır";
            if (!products.TryGetValue(l.ProductId, out var product)) throw PosErrors.Bad("product_not_found", $"{label}: kalem bulunamadı.");
            if (!product.IsActive) throw PosErrors.Bad("product_inactive", $"{label}: kalem pasif.");
            if (l.VariantId is { } vid && (!variants.TryGetValue(vid, out var variant) || variant.ProductId != product.Id))
                throw PosErrors.Bad("variant_invalid", $"{label}: varyant kaleme ait değil.");
            if (product.Kind != ProductKind.Service && TrackingValidator.IsTracked(product))
                throw PosErrors.Bad("tracking_unsupported", $"{label}: lot/seri takipli kalem kasadan satılamaz.");
            if (l.Quantity <= 0m || Math.Round(l.Quantity, 6) != l.Quantity) throw PosErrors.Bad("quantity_invalid", $"{label}: miktar sıfırdan büyük olmalı ve en fazla 6 ondalık basamak içermelidir.");

            var (unit, factor) = ResolveUnit(l.Unit, product, label);
            var vatRate        = product.VatRate;
            if (!InvoiceCalculator.AllowedVatRates.Contains(vatRate)) throw PosErrors.Bad("invalid_vat_rate", $"{label}: KDV oranı geçersiz.");

            var quote = await quotes.QuoteAsync(new QuoteRequest(PriceListKind.Sale, customerId, product.Id, l.VariantId, unit, l.Quantity, date), ct);
            decimal price;
            bool    vatIncluded;
            if (quote is not null) {
                if (!string.Equals(quote.Currency, "TRY", StringComparison.OrdinalIgnoreCase))
                    throw PosErrors.Bad("price_currency", $"{label}: kasada yalnızca TL fiyatlı kalemler satılır.");
                (price, vatIncluded) = (quote.UnitPrice, quote.VatIncluded);
            } else if (l.UnitPrice is { } given && given >= 0m && Math.Round(given, 6) == given) {
                // Fiyat listesinde olmayan (açık fiyatlı) kalem: kasiyerin girdiği KDV dahil fiyat.
                (price, vatIncluded) = (given, true);
            } else {
                throw PosErrors.Bad("price_required", $"{label}: satış fiyatı bulunamadı.");
            }

            if (l.FixedTotal is not null && l.ManualDiscountPercent is > 0m)
                throw PosErrors.Bad("fixed_total_discount", $"{label}: terazi fiyatlı satırda iskonto yapılamaz.");
            prepared.Add(new Prepared(n + 1, product, l.VariantId, unit, factor, l.Quantity, price, vatIncluded, vatRate, l.ManualDiscountPercent, l.FixedTotal));
        }

        // Otomatik kampanya: satır başına en iyi tek kampanya. Terazi fiyatlı satırda kampanya yoktur.
        var results = await campaigns.EvaluateAsync(prepared.Select(p => new CampaignLine(p.Product.Id, p.VariantId, null, p.Quantity, p.Price)).ToList(), time.GetUtcNow(), ct);

        var lines = new List<ResolvedSaleLine>();
        for (var i = 0; i < prepared.Count; i++) {
            var p     = prepared[i];
            var label = $"{p.Sort}. satır";
            var name  = p.Product.Name;
            lines.Add(p.FixedTotal is { } fixedTotal
                          ? FixedLine(p, name, fixedTotal, label)
                          : PricedLine(p, name, results[i], label));
        }

        return new ResolvedSale(lines);
    }

    private ResolvedSaleLine PricedLine(Prepared p, string name, CampaignResult? campaign, string label) {
        var unitNet = p.VatIncluded ? Round(p.Price / (1m + p.VatRate / 100m), 6) : p.Price;
        var gross   = Round(p.Quantity * unitNet, 2);

        // Kampanya tutarı fiyatın cinsindendir (KDV dahil fiyatsa KDV dahil); KDV hariç tutara çevrilir.
        var campaignNet = campaign is null ? 0m : Round(p.VatIncluded ? campaign.DiscountAmount / (1m + p.VatRate / 100m) : campaign.DiscountAmount, 2);
        campaignNet = Math.Min(campaignNet, gross);

        var manual = 0m;
        if (p.ManualPercent is { } pct and > 0m) {
            if (pct > 100m) throw PosErrors.Bad("discount_invalid", $"{label}: iskonto yüzdesi 100'ü aşamaz.");
            manual = Round((gross - campaignNet) * pct / 100m, 2);
            // İskonto politikası yalnızca elle iskontoya uygulanır (otomatik kampanya kasiyerin yetkisine bağlı değildir).
            if (gross > 0m) DiscountPolicy.Ensure(Math.Round(manual / gross * 100m, 4), tenant);
        }

        var net = gross - campaignNet - manual;
        var vat = Round(net * p.VatRate / 100m, 2);
        return new ResolvedSaleLine(p.Sort, p.Product, p.VariantId, name, p.Unit, p.Factor, p.Quantity, unitNet, p.VatRate, manual, campaignNet,
                                    campaignNet > 0m ? campaign?.CampaignId : null, net, vat, net + vat);
    }

    /// <summary>
    ///     Terazi fiyat tipi: satır toplamı barkoddaki tutardır. Tutar, sunucu fiyatı x miktardan sapmamalıdır (miktar 3 haneye yuvarlandığı için en çok
    ///     yarım binde birim fiyat + 1 kuruş). Fatura hesabıyla (net + KDV) tam tutturulabilen en yakın net seçilir; birim fiyat brütü net'e en yakın
    ///     (üstten) çıkacak şekilde türetilir ve aradaki kuruş farkı iskonto olarak yazılır.
    /// </summary>
    private static ResolvedSaleLine FixedLine(Prepared p, string name, decimal fixedTotal, string label) {
        if (fixedTotal <= 0m || Round(fixedTotal, 2) != fixedTotal) throw PosErrors.Bad("fixed_total_invalid", $"{label}: terazi tutarı geçersiz.");

        var unitGross = p.VatIncluded ? p.Price : p.Price * (1m + p.VatRate / 100m);
        if (Math.Abs(fixedTotal - p.Quantity * unitGross) > unitGross * 0.0005m + 0.01m)
            throw PosErrors.Bad("fixed_total_invalid", $"{label}: terazi tutarı miktar ve fiyatla tutarlı değil.");

        var rate = p.VatRate / 100m;
        var net  = Round(fixedTotal / (1m + rate), 2);
        var best = net;
        var gap  = decimal.MaxValue;
        for (var candidate = net - 0.03m; candidate <= net + 0.03m; candidate += 0.01m) {
            if (candidate < 0m) continue;
            var total = candidate + Round(candidate * rate, 2);
            var diff  = Math.Abs(total - fixedTotal);
            if (diff < gap) (gap, best) = (diff, candidate);
            if (diff == 0m) break;
        }

        var vat = Round(best * rate, 2);

        // Fatura satırı: birim fiyat (6 hane, yukarı yuvarlanmış) x miktar >= net; fark iskonto olarak yazılır, faturada net aynen çıkar.
        var unitNet = Math.Ceiling(best / p.Quantity * 1_000_000m) / 1_000_000m;
        var gross   = Round(p.Quantity * unitNet, 2);
        return new ResolvedSaleLine(p.Sort, p.Product, p.VariantId, name, p.Unit, p.Factor, p.Quantity, unitNet, p.VatRate, gross - best, 0m, null, best, vat, best + vat);
    }

    private static (string Unit, decimal Factor) ResolveUnit(string? requested, Product product, string label) {
        var wanted = requested?.Trim();
        if (string.IsNullOrEmpty(wanted) || string.Equals(wanted, product.BaseUnit, StringComparison.OrdinalIgnoreCase)) return (product.BaseUnit, 1m);
        var unit = product.Units.FirstOrDefault(u => string.Equals(u.Unit, wanted, StringComparison.OrdinalIgnoreCase))
                   ?? throw PosErrors.Bad("unit_invalid", $"{label}: \"{wanted}\" birimi kalemde tanımlı değil.");
        return (unit.Unit, unit.Factor);
    }

    private static decimal Round(decimal value, int decimals = 2) {
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
    }

    private sealed record Prepared(
        int Sort, Product Product, Guid? VariantId, string Unit, decimal Factor, decimal Quantity, decimal Price, bool VatIncluded, int VatRate, decimal? ManualPercent, decimal? FixedTotal);
}
