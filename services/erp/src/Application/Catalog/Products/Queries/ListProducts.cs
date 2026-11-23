using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Catalog.Products.Queries;

[Authorize(Permission = CatalogPermissions.Read)]
public sealed record ListProductsQuery(
    string?      Search,
    string?      Kind,
    Guid?        CategoryId,
    Guid?        BrandId,
    string?      Tag,
    bool         IncludeInactive,
    int          Page     = 1,
    int          PageSize = 25
) : IRequest<PagedResult<ProductListItem>>;

public sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery> {
    public ListProductsQueryValidator() {
        RuleFor(q => q.Kind).Must(k => k is "product" or "service").When(q => !string.IsNullOrEmpty(q.Kind))
            .WithMessage("Tür ürün (product) veya hizmet (service) olmalıdır.");
        RuleFor(q => q.Page).InclusiveBetween(1, 100000).WithMessage("Sayfa 1 ile 100000 arasında olmalıdır.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100).WithMessage("Sayfa boyutu 1 ile 100 arasında olmalıdır.");
    }
}

public sealed class ListProductsQueryHandler(
    IApplicationDbContext db
) : IRequestHandler<ListProductsQuery, PagedResult<ProductListItem>> {
    public async Task<PagedResult<ProductListItem>> Handle(ListProductsQuery request, CancellationToken ct) {
        var query = db.Products.AsNoTracking().AsQueryable();

        if (!request.IncludeInactive) query = query.Where(p => p.IsActive);
        if (request.Kind == "product") query      = query.Where(p => p.Kind == ProductKind.Product);
        else if (request.Kind == "service") query = query.Where(p => p.Kind == ProductKind.Service);
        if (request.BrandId is { } brandId) query = query.Where(p => p.BrandId == brandId);

        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        if (request.CategoryId is { } categoryId) {
            // Seçilen kategori ve alt kategorileri.
            var ids   = new HashSet<Guid> { categoryId };
            var added = true;
            while (added) {
                added = false;
                foreach (var c in categories.Values)
                    if (c.ParentId is { } pid && ids.Contains(pid) && ids.Add(c.Id))
                        added = true;
            }

            query = query.Where(p => p.CategoryId != null && ids.Contains(p.CategoryId.Value));
        }

        if (!string.IsNullOrWhiteSpace(request.Tag)) {
            var tag = request.Tag.Trim();
            query = query.Where(p => p.Tags.Contains(tag));
        }

        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = TurkishText.Fold(request.Search);
            query = query.Where(p => p.Name.ToLower().Replace("ı", "i").Contains(term)
                                     || p.Code.ToLower().Replace("ı", "i").Contains(term)
                                     || db.Barcodes.Any(b => b.ProductId == p.Id && b.Value.Contains(term))
                                     || p.Suppliers.Any(s => s.SupplierCode != null
                                                             && s.SupplierCode.ToLower().Replace("ı", "i")
                                                                 .Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var page = await query.OrderBy(p => p.Code).Skip((request.Page - 1) * request.PageSize)
                       .Take(request.PageSize).Include(p => p.Images).ToListAsync(ct);

        var pageIds = page.Select(p => p.Id).ToList();
        var barcodes = await db.Barcodes.AsNoTracking().Where(b => pageIds.Contains(b.ProductId) && b.Owner == BarcodeOwner.Product)
                           .ToDictionaryAsync(b => b.ProductId, b => b.Value, ct);
        var variantCounts = await db.ProductVariants.AsNoTracking().Where(v => pageIds.Contains(v.ProductId))
                                .GroupBy(v => v.ProductId).Select(g => new { g.Key, Count = g.Count() })
                                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var brandIds = page.Where(p => p.BrandId != null).Select(p => p.BrandId!.Value).Distinct().ToList();
        var brands = await db.Brands.AsNoTracking().Where(b => brandIds.Contains(b.Id))
                         .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var items = page.Select(p => new ProductListItem(
                                    p.Id, p.Kind, p.Code, p.Name, p.CategoryId,
                                    ProductReader.PathOf(p.CategoryId, categories), p.BrandId,
                                    p.BrandId is { } b ? brands.GetValueOrDefault(b) : null, p.BaseUnit, p.VatRate,
                                    p.IsActive, barcodes.GetValueOrDefault(p.Id), p.DefaultSalePrice,
                                    variantCounts.GetValueOrDefault(p.Id), p.Tags.ToArray(),
                                    p.Images.OrderBy(i => i.Sort).Select(i => (Guid?)i.FileId).FirstOrDefault()))
            .ToList();

        return new PagedResult<ProductListItem>(items, total);
    }
}
