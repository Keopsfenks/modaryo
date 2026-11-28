using ERP.Application.Catalog.Items;
using ERP.Application.Catalog.Items.Queries;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Pricing;
using ERP.Application.Pricing.Campaigns;
using ERP.Application.Pricing.Quotes;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;
using ERP.Domain.Pos;
using ERP.Domain.Pricing;

namespace ERP.Application.Pos.Lookup;

public sealed record OtherStoreDto(Guid BranchId, string BranchName, string Unit, decimal Available);

/// <summary>
///     Kasa ürün sorgusu sonucu. UnitPrice, P2 satış önceliğiyle (perakende müşteri) bulunan birim fiyattır; VatIncluded fiyatın KDV
///     dahil olup olmadığını söyler. Fiyat tipi terazi barkodunda FixedTotal, barkoddaki (KDV dahil) tutardır: satır toplamı
///     miktar x fiyat yerine bu tutara eşitlenir.
/// </summary>
public sealed record PosLookupDto(
    SellableItem                Item,
    string                      Unit,
    decimal                     Factor,
    decimal                     Quantity,
    decimal                     UnitPrice,
    bool                        VatIncluded,
    decimal?                    FixedTotal,
    CampaignResult?             Campaign,
    IReadOnlyList<OtherStoreDto> OtherStores
);

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Sell)]
public sealed record PosLookupQuery(string Code, Guid? RegisterId) : IRequest<PosLookupDto>;

public sealed class PosLookupQueryValidator : AbstractValidator<PosLookupQuery> {
    public PosLookupQueryValidator() {
        RuleFor(q => q.Code).Must(c => !string.IsNullOrWhiteSpace(c) && c.Trim().Length <= 100).WithMessage("Barkod ya da kod 1 ile 100 karakter arasında olmalıdır.");
    }
}

/// <summary>
///     Çözüm sırası: terazi barkodu, katalog barkodu (varyant, birim, ürün), kalem ya da varyant kodu. Fiyat ve kampanya perakende müşteri
///     için bugünün tarihine göre bulunur. Diğer şubelerin varsayılan deposundaki kullanılabilir miktar yalnızca miktar olarak gösterilir
///     (şube erişimi gerekmez).
/// </summary>
public sealed class PosLookupQueryHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    ICurrentTenant        tenant,
    IPriceQuoteService    quotes,
    ICampaignService      campaigns,
    IBranchDirectory      branches,
    PosSetup              setup,
    TimeProvider          time
) : IRequestHandler<PosLookupQuery, PosLookupDto> {
    public async Task<PosLookupDto> Handle(PosLookupQuery request, CancellationToken ct) {
        var code     = request.Code.Trim();
        var settings = (PosSettings)(await setup.EnsureAsync(ct)).Value;
        var retail   = settings.RetailCustomerId!.Value;

        var (hit, scale) = await ResolveAsync(code, settings, ct);
        var item         = PurchaseVisibility.Mask(hit.Item, PurchaseVisibility.CanSee(session));
        if (!item.IsActive) throw PosErrors.Conflict("item_inactive", "Kalem pasif; satılamaz.");

        decimal quantity = 1m;
        if (scale?.WeightKg is { } kg) quantity = IsGramUnit(hit.Unit) ? kg * 1000m : kg;

        var quote = await quotes.QuoteAsync(new QuoteRequest(PriceListKind.Sale, retail, item.ProductId, item.VariantId, hit.Unit, quantity, CurrentAccountClock.Today(time)), ct)
                    ?? throw PosErrors.Bad("price_required", "Bu kalem için satış fiyatı bulunamadı.");

        decimal? fixedTotal = null;
        if (scale?.PriceTry is { } price) {
            var gross = quote.VatIncluded ? quote.UnitPrice : quote.UnitPrice * (1m + item.VatRate / 100m);
            if (gross <= 0m) throw PosErrors.Bad("price_required", "Bu kalem için satış fiyatı bulunamadı.");
            quantity   = Math.Round(price / gross, 3, MidpointRounding.AwayFromZero);
            fixedTotal = price;
        }

        var campaign = (await campaigns.EvaluateAsync([new CampaignLine(item.ProductId, item.VariantId, null, quantity, quote.UnitPrice)], time.GetUtcNow(), ct))[0];
        var others   = await OtherStoresAsync(item, request.RegisterId, ct);

        return new PosLookupDto(item, hit.Unit, hit.Factor, quantity, quote.UnitPrice, quote.VatIncluded, fixedTotal, campaign, others);
    }

    private static bool IsGramUnit(string unit) {
        return unit.Equals("g", StringComparison.OrdinalIgnoreCase) || unit.Equals("gr", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(BarcodeHit Hit, ScaleHit? Scale)> ResolveAsync(string code, PosSettings settings, CancellationToken ct) {
        var formats = settings.ScaleFormats;
        var scale   = ScaleBarcode.TryParse(code, formats);
        if (scale is null && code.Length == ScaleBarcode.Ean13Length && code.All(char.IsAsciiDigit) && formats.Any(f => ScaleBarcode.Matches(code, f)))
            throw PosErrors.ScaleBarcodeInvalid();

        if (scale is not null) {
            var itemHit = await FindByCodeAsync(scale.ItemCode, ct) ?? await FindByBarcodeAsync(scale.ItemCode, ct)
                          ?? throw new NotFoundException(code, "Item");
            // Terazi kalemi temel birimde satılır; birim barkoduyla eşleşse bile miktar ağırlık/tutardan gelir.
            return (itemHit with { Unit = itemHit.Item.BaseUnit, Factor = 1m }, scale);
        }

        var hit = await FindByBarcodeAsync(code, ct) ?? await FindByCodeAsync(code, ct) ?? throw new NotFoundException(code, "Item");
        return (hit, null);
    }

    private async Task<BarcodeHit?> FindByBarcodeAsync(string value, CancellationToken ct) {
        try {
            return await BarcodeResolver.FindAsync(db, value, ct);
        } catch (NotFoundException) {
            return null;
        }
    }

    /// <summary>Varyantsız kartın kodu ya da varyant kodu; varyantlı kartın kendisi satılamaz.</summary>
    private async Task<BarcodeHit?> FindByCodeAsync(string code, CancellationToken ct) {
        var term = ERP.Domain.Common.TurkishText.Fold(code);
        var product = await db.Products.AsNoTracking().Include(p => p.Variants).Include(p => p.Units)
                          .Where(p => p.Code.ToLower().Replace("ı", "i") == term && !p.Variants.Any())
                          .FirstOrDefaultAsync(ct);
        if (product is not null) return new BarcodeHit(SellableItem.From(product, null), product.BaseUnit, 1m);

        product = await db.Products.AsNoTracking().Include(p => p.Variants).Include(p => p.Units)
                      .Where(p => p.Variants.Any(v => v.Code.ToLower().Replace("ı", "i") == term))
                      .FirstOrDefaultAsync(ct);
        var variant = product?.Variants.FirstOrDefault(v => ERP.Domain.Common.TurkishText.Fold(v.Code) == term);
        return product is null || variant is null ? null : new BarcodeHit(SellableItem.From(product, variant), product.BaseUnit, 1m);
    }

    private async Task<IReadOnlyList<OtherStoreDto>> OtherStoresAsync(SellableItem item, Guid? registerId, CancellationToken ct) {
        Guid? exclude = null;
        if (registerId is { } rid)
            exclude = await db.PosRegisters.AsNoTracking().Where(r => r.Id == rid).Select(r => (Guid?)r.BranchId).FirstOrDefaultAsync(ct);

        var warehouses = await db.Warehouses.AsNoTracking().Where(w => w.IsDefault && w.IsActive && w.Kind == WarehouseKind.Normal && w.BranchId != exclude).ToListAsync(ct);
        if (warehouses.Count == 0) return [];

        var ids = warehouses.Select(w => w.Id).ToList();
        var physical = await db.StockMovements.AsNoTracking()
                           .Where(m => ids.Contains(m.WarehouseId) && m.ProductId == item.ProductId && m.VariantId == item.VariantId)
                           .GroupBy(m => m.WarehouseId).Select(g => new { g.Key, Qty = g.Sum(m => m.Quantity) }).ToDictionaryAsync(g => g.Key, g => g.Qty, ct);
        var reserved = await db.Reservations.AsNoTracking()
                           .Where(r => ids.Contains(r.WarehouseId) && r.ProductId == item.ProductId && r.VariantId == item.VariantId
                                       && r.Status == ReservationStatus.Active && r.ExpectedReceiptSourceId == null)
                           .GroupBy(r => r.WarehouseId).Select(g => new { g.Key, Qty = g.Sum(r => r.Quantity) }).ToDictionaryAsync(g => g.Key, g => g.Qty, ct);

        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var names    = (await branches.ListAsync(tenantId, ct)).Where(b => b.IsActive).ToDictionary(b => b.Id, b => b.Name);
        var result   = new List<OtherStoreDto>();
        foreach (var w in warehouses) {
            if (!names.TryGetValue(w.BranchId, out var name)) continue;
            var available = physical.GetValueOrDefault(w.Id) - reserved.GetValueOrDefault(w.Id);
            if (available > 0m) result.Add(new OtherStoreDto(w.BranchId, name, item.BaseUnit, available));
        }

        return result.OrderBy(r => r.BranchName, StringComparer.Ordinal).ToList();
    }
}

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Sell)]
public sealed record PosSearchQuery(string? Search) : IRequest<IReadOnlyList<SellableItem>>;

/// <summary>Kasadan kalem arama (en çok 20, pasifler hariç). Katalog izni gerekmez; alış fiyatı fiyat.alis iznine göre maskelenir.</summary>
public sealed class PosSearchQueryHandler(IApplicationDbContext db, ICurrentSession session) : IRequestHandler<PosSearchQuery, IReadOnlyList<SellableItem>> {
    public async Task<IReadOnlyList<SellableItem>> Handle(PosSearchQuery request, CancellationToken ct) {
        return await new SearchItemsQueryHandler(db, session).Handle(new SearchItemsQuery(request.Search, null, false, 20), ct);
    }
}
