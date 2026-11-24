using ERP.Application.Catalog.Products.Queries;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Catalog;

namespace ERP.Application.Catalog.Excel.Queries;

/// <summary>Kind: product | service (boşsa hepsi). Pasifler varsayılan olarak dahildir; dışa alınan dosya olduğu gibi içe alınabilsin.</summary>
[Authorize(Permission = CatalogPermissions.Read)]
public sealed record ExportProductsQuery(string? Kind, bool IncludeInactive = true) : IRequest<byte[]>;

public sealed class ExportProductsQueryHandler(
    IApplicationDbContext db,
    IXlsxService          xlsx,
    ICurrentSession       session
) : IRequestHandler<ExportProductsQuery, byte[]> {
    public async Task<byte[]> Handle(ExportProductsQuery request, CancellationToken ct) {
        var query = db.Products.AsNoTracking().AsQueryable();
        if (!request.IncludeInactive) query = query.Where(p => p.IsActive);
        if (request.Kind == "product") query      = query.Where(p => p.Kind == ProductKind.Product);
        else if (request.Kind == "service") query = query.Where(p => p.Kind == ProductKind.Service);

        var products   = await query.OrderBy(p => p.Code).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var brands     = await db.Brands.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var barcodes = await db.Barcodes.AsNoTracking().Where(b => b.Owner == BarcodeOwner.Product)
                           .ToDictionaryAsync(b => b.ProductId, b => b.Value, ct);

        var rows = products.Select(p => (IReadOnlyList<object?>)new object?[] {
            p.Code, p.Name, ProductSheet.KindText(p.Kind), ProductReader.PathOf(p.CategoryId, categories),
            p.BrandId is { } b ? brands.GetValueOrDefault(b) : null, p.BaseUnit, p.VatRate, barcodes.GetValueOrDefault(p.Id),
            p.DefaultPurchasePrice?.Amount, p.DefaultPurchasePrice?.Currency,
            p.DefaultPurchasePrice is { } pp ? ProductSheet.Flag(pp.VatIncluded) : null,
            p.DefaultSalePrice?.Amount, p.DefaultSalePrice?.Currency,
            p.DefaultSalePrice is { } sp ? ProductSheet.Flag(sp.VatIncluded) : null,
            string.Join(", ", p.Tags), ProductSheet.Flag(!p.IsActive)
        });

        // fiyat.alis izni yoksa alış sütunları dosyaya hiç yazılmaz (başlık da dahil).
        if (ERP.Application.Pricing.PurchaseVisibility.CanSee(session))
            return xlsx.Write(ProductSheet.SheetName, ProductSheet.Headers, rows);

        return xlsx.Write(ProductSheet.SheetName, ProductSheet.HeadersWithoutPurchase,
                          rows.Select(r => (IReadOnlyList<object?>)r.Where((_, i) => !ProductSheet.PurchaseColumns.Contains(i)).ToArray()));
    }
}

[Authorize(Permission = CatalogPermissions.Read)]
public sealed record ImportTemplateQuery : IRequest<byte[]>;

public sealed class ImportTemplateQueryHandler(
    IXlsxService    xlsx,
    ICurrentSession session
) : IRequestHandler<ImportTemplateQuery, byte[]> {
    public Task<byte[]> Handle(ImportTemplateQuery request, CancellationToken ct) {
        var headers = ERP.Application.Pricing.PurchaseVisibility.CanSee(session) ? ProductSheet.Headers : ProductSheet.HeadersWithoutPurchase;
        return Task.FromResult(xlsx.Write(ProductSheet.SheetName, headers, []));
    }
}
