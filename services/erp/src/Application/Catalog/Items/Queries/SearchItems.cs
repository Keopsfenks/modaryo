using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Catalog.Items.Queries;

[Authorize(Permission = CatalogPermissions.Read)]
public sealed record SearchItemsQuery(
    string? Search,
    string? Kind,
    bool    IncludeInactive,
    int     Limit = 20
) : IRequest<IReadOnlyList<SellableItem>>;

public sealed class SearchItemsQueryValidator : AbstractValidator<SearchItemsQuery> {
    public SearchItemsQueryValidator() {
        RuleFor(q => q.Kind).Must(k => k is "product" or "service").When(q => !string.IsNullOrEmpty(q.Kind))
            .WithMessage("Tür ürün (product) veya hizmet (service) olmalıdır.");
        RuleFor(q => q.Limit).InclusiveBetween(1, 50).WithMessage("Sınır 1 ile 50 arasında olmalıdır.");
    }
}

/// <summary>
///     Varyantsız kartı tek kalem, varyantlı kartı varyant başına bir kalem olarak düzleştirir (kartın kendisi satılamaz).
///     Arama kod, ad, barkod (her sahip) ve tedarikçi kodunda Türkçe harf katlamalıdır; yalnızca varyant kodu ya da
///     barkoduyla eşleşen kartta yalnızca o varyantlar döner.
/// </summary>
public sealed class SearchItemsQueryHandler(
    IApplicationDbContext db,
    ICurrentSession       session
) : IRequestHandler<SearchItemsQuery, IReadOnlyList<SellableItem>> {
    public async Task<IReadOnlyList<SellableItem>> Handle(SearchItemsQuery request, CancellationToken ct) {
        var query = db.Products.AsNoTracking().AsQueryable();
        if (!request.IncludeInactive) query = query.Where(p => p.IsActive);
        if (request.Kind == "product") query      = query.Where(p => p.Kind == ProductKind.Product);
        else if (request.Kind == "service") query = query.Where(p => p.Kind == ProductKind.Service);

        var term = string.IsNullOrWhiteSpace(request.Search) ? null : TurkishText.Fold(request.Search);
        if (term is not null)
            query = query.Where(p => p.Name.ToLower().Replace("ı", "i").Contains(term)
                                     || p.Code.ToLower().Replace("ı", "i").Contains(term)
                                     || db.Barcodes.Any(b => b.ProductId == p.Id && b.Value.Contains(term))
                                     || p.Suppliers.Any(s => s.SupplierCode != null
                                                             && s.SupplierCode.ToLower().Replace("ı", "i").Contains(term))
                                     || p.Variants.Any(v => v.Code.ToLower().Replace("ı", "i").Contains(term)));

        var products = await query.OrderBy(p => p.Code).Take(request.Limit).Include(p => p.Variants)
                           .Include(p => p.Units).ToListAsync(ct);

        // Kartın kendisi (ad, kod, tedarikçi kodu, ürün/birim barkodu) eşleşen kartlarda tüm varyantlar gelir.
        var cardMatches    = new HashSet<Guid>();
        var variantMatches = new HashSet<Guid>();
        if (term is not null && products.Count > 0) {
            var ids = products.Select(p => p.Id).ToList();
            cardMatches = (await db.Products.AsNoTracking()
                               .Where(p => ids.Contains(p.Id)
                                           && (p.Name.ToLower().Replace("ı", "i").Contains(term)
                                               || p.Code.ToLower().Replace("ı", "i").Contains(term)
                                               || db.Barcodes.Any(b => b.ProductId == p.Id
                                                                       && b.Owner != BarcodeOwner.Variant
                                                                       && b.Value.Contains(term))
                                               || p.Suppliers.Any(s => s.SupplierCode != null
                                                                       && s.SupplierCode.ToLower().Replace("ı", "i")
                                                                           .Contains(term))))
                               .Select(p => p.Id).ToListAsync(ct)).ToHashSet();
            variantMatches = (await db.Barcodes.AsNoTracking()
                                  .Where(b => ids.Contains(b.ProductId) && b.Owner == BarcodeOwner.Variant
                                              && b.Value.Contains(term))
                                  .Select(b => b.OwnerId).ToListAsync(ct)).ToHashSet();
        }

        var canSee = ERP.Application.Pricing.PurchaseVisibility.CanSee(session);
        var items  = new List<SellableItem>();
        foreach (var product in products) {
            if (product.Variants.Count == 0) {
                items.Add(ERP.Application.Pricing.PurchaseVisibility.Mask(SellableItem.From(product, null), canSee));
                continue;
            }

            foreach (var variant in product.Variants.OrderBy(v => v.Code, StringComparer.Ordinal)) {
                if (!request.IncludeInactive && !variant.IsActive) continue;
                if (term is not null && !cardMatches.Contains(product.Id)
                                     && !(TurkishText.Fold(variant.Code).Contains(term)
                                          || variantMatches.Contains(variant.Id)))
                    continue;

                items.Add(ERP.Application.Pricing.PurchaseVisibility.Mask(SellableItem.From(product, variant), canSee));
            }
        }

        return items.Take(request.Limit).ToList();
    }
}
