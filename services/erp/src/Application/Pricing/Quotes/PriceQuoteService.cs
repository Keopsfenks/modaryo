using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;
using ERP.Domain.Pricing;

namespace ERP.Application.Pricing.Quotes;

public sealed class PriceQuoteService(
    IApplicationDbContext db
) : IPriceQuoteService {
    private sealed record ProductData(
        Guid                                               Id,
        string                                             BaseUnit,
        string?                                            MeasureUnit,
        Money4?                                            Sale,
        Money4?                                            Purchase,
        Dictionary<string, decimal>                        Factors,
        Dictionary<Guid, (Money4? Sale, Money4? Purchase)> Variants,
        List<CustomerPrice>                                CustomerPrices
    );

    public async Task<PriceQuote?> QuoteAsync(QuoteRequest request, CancellationToken ct) {
        return (await QuoteManyAsync([request], ct))[0];
    }

    public async Task<IReadOnlyList<PriceQuote?>> QuoteManyAsync(IReadOnlyList<QuoteRequest> requests, CancellationToken ct) {
        if (requests.Count == 0) return [];

        var productIds = requests.Select(r => r.ProductId).Distinct().ToList();
        var variantIds = requests.Where(r => r.VariantId is not null).Select(r => r.VariantId!.Value).Distinct().ToList();
        var accountIds = requests.Where(r => r.CurrentAccountId is not null).Select(r => r.CurrentAccountId!.Value).Distinct().ToList();

        // Sorgu 1: kartlar (birimler, varyant fiyatları) ve müşteriye özel fiyatlar.
        var rawProducts = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
                              .Select(p => new {
                                  p.Id, p.BaseUnit, p.MeasureUnit, p.DefaultSalePrice, p.DefaultPurchasePrice,
                                  Units = p.Units.Select(u => new { u.Unit, u.Factor }).ToList(),
                                  Variants = p.Variants.Where(v => variantIds.Contains(v.Id))
                                      .Select(v => new { v.Id, v.SalePriceOverride, v.PurchasePriceOverride }).ToList(),
                                  Prices = db.CustomerPrices.Where(c => c.ProductId == p.Id && accountIds.Contains(c.CurrentAccountId))
                                      .ToList()
                              }).ToListAsync(ct);
        var products = rawProducts.ToDictionary(
            p => p.Id,
            p => new ProductData(p.Id, p.BaseUnit, p.MeasureUnit, p.DefaultSalePrice, p.DefaultPurchasePrice,
                                 p.Units.ToDictionary(u => u.Unit, u => u.Factor),
                                 p.Variants.ToDictionary(v => v.Id, v => (v.SalePriceOverride, v.PurchasePriceOverride)),
                                 p.Prices));

        // Sorgu 2: listeler, istenen kalemlerin satırları, grup listesi sahibi müşteriler ve fiyat ayarları.
        var rawLists = await db.PriceLists.AsNoTracking()
                           .Select(l => new {
                               List = l,
                               Rows = db.PriceListItems.Where(i => i.PriceListId == l.Id && productIds.Contains(i.ProductId)).ToList(),
                               AccountIds = db.CurrentAccounts
                                   .Where(a => accountIds.Contains(a.Id)
                                               && db.CustomerGroups.Any(g => g.Id == a.CustomerGroupId && g.SalePriceListId == l.Id))
                                   .Select(a => a.Id).ToList(),
                               Settings = db.TenantSettings.Where(s => s.Category == "fiyat").Select(s => s.Json).ToList()
                           }).ToListAsync(ct);
        var lists    = rawLists.ToDictionary(l => l.List.Id, l => l.List);
        var rows     = rawLists.SelectMany(l => l.Rows).ToList();
        var settings = rawLists.SelectMany(l => l.Settings).FirstOrDefault() is { } json
                           ? JsonSerializer.Deserialize<PricingSettings>(json, SettingsRegistry.Json) ?? new PricingSettings()
                           : new PricingSettings();
        var graph = new PriceGraph(lists, rows, settings.Rounding);

        var result = new List<PriceQuote?>(requests.Count);
        foreach (var request in requests) {
            if (!products.TryGetValue(request.ProductId, out var product)) {
                result.Add(null);
                continue;
            }

            Guid? groupListId = request.Kind == PriceListKind.Sale && request.CurrentAccountId is { } account
                                    ? rawLists.FirstOrDefault(l => l.AccountIds.Contains(account))?.List.Id
                                    : null;
            result.Add(Resolve(request, product, lists, graph, settings, groupListId));
        }

        return result;
    }

    private static PriceQuote? Resolve(QuoteRequest request, ProductData product, IReadOnlyDictionary<Guid, PriceList> lists,
                                       PriceGraph graph, PricingSettings settings, Guid? groupListId) {
        decimal factor;
        if (request.Unit == product.BaseUnit) factor = 1m;
        else if (product.Factors.TryGetValue(request.Unit, out var f)) factor = f;
        else return null;

        var priority = request.Kind == PriceListKind.Sale ? PriceResolver.SalePriority : PriceResolver.PurchasePriority;

        var exact = PriceResolver.Resolve(Candidates(request, product, lists, graph, settings, groupListId, request.Unit, factor == 1m),
                                          priority, request.Quantity, request.Date);
        if (exact is not null || factor == 1m) return exact;

        // Birimde fiyat yoksa temel birim fiyatı birim katsayısıyla çarpılır; miktar da temel birime çevrilir.
        var baseQuote = PriceResolver.Resolve(
            Candidates(request, product, lists, graph, settings, groupListId, product.BaseUnit, true), priority,
            request.Quantity * factor, request.Date);
        return baseQuote is null ? null : baseQuote with { UnitPrice = Math.Round(baseQuote.UnitPrice * factor, 4) };
    }

    private static List<PriceCandidate> Candidates(QuoteRequest request, ProductData product, IReadOnlyDictionary<Guid, PriceList> lists,
                                                   PriceGraph graph, PricingSettings settings, Guid? groupListId, string unit,
                                                   bool isBaseUnit) {
        var result = new List<PriceCandidate>();
        var sale   = request.Kind == PriceListKind.Sale;

        void AddList(PriceSource source, Guid? listId) {
            if (listId is not { } id || !lists.TryGetValue(id, out var list) || !list.IsActive || list.Kind != request.Kind) return;

            if (request.VariantId is { } variantId && graph.TiersFor(id, product.Id, variantId, unit) is { } variantTiers)
                result.Add(new PriceCandidate(source, id, true, list.Currency, list.VatIncluded, variantTiers.Tiers, list.ValidFrom,
                                              list.ValidTo));
            if (graph.TiersFor(id, product.Id, null, unit) is { } tiers)
                result.Add(new PriceCandidate(source, id, false, list.Currency, list.VatIncluded, tiers.Tiers, list.ValidFrom,
                                              list.ValidTo));
        }

        if (sale) {
            foreach (var cp in product.CustomerPrices.Where(c => c.CurrentAccountId == request.CurrentAccountId && c.Unit == unit
                                                                 && (c.VariantId is null || c.VariantId == request.VariantId)))
                result.Add(new PriceCandidate(PriceSource.CustomerPrice, cp.Id, cp.VariantId is not null, cp.Currency, cp.VatIncluded,
                                              cp.Tiers, cp.ValidFrom, cp.ValidTo));
            AddList(PriceSource.GroupList, groupListId);
            AddList(PriceSource.DefaultList, settings.DefaultSaleListId);
        } else {
            foreach (var list in lists.Values.Where(l => l.SupplierId is not null && l.SupplierId == request.CurrentAccountId))
                AddList(PriceSource.SupplierList, list.Id);
            AddList(PriceSource.DefaultList, settings.DefaultPurchaseListId);
        }

        // Kart varsayılanı yalnızca temel birim içindir; diğer birimler temel birim yedeğiyle çözülür.
        if (isBaseUnit) {
            var card = sale ? product.Sale : product.Purchase;
            if (request.VariantId is { } vid && product.Variants.TryGetValue(vid, out var overrides)
                                             && (sale ? overrides.Sale : overrides.Purchase) is { } over)
                result.Add(CardCandidate(over, true));
            if (card is not null) result.Add(CardCandidate(card, false));
        }

        return result;
    }

    private static PriceCandidate CardCandidate(Money4 money, bool variantSpecific) {
        return new PriceCandidate(PriceSource.CardDefault, null, variantSpecific, money.Currency, money.VatIncluded,
                                  [new PriceTier(1m, money.Amount)], null, null);
    }
}
