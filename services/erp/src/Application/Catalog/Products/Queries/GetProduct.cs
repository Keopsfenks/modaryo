using System.Text.Json;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Catalog;

namespace ERP.Application.Catalog.Products.Queries;

public static class ProductReader {
    /// <summary>Kategorinin kökten kendisine kadar yolu ("Giyim > Erkek > Tişört").</summary>
    public static string? PathOf(Guid? categoryId, IReadOnlyDictionary<Guid, Category> all) {
        if (categoryId is null) return null;

        var names   = new List<string>();
        var visited = new HashSet<Guid>();
        for (var id = categoryId; id is { } current && visited.Add(current) && all.TryGetValue(current, out var node);
             id = node!.ParentId)
            names.Insert(0, node!.Name);

        return names.Count == 0 ? null : string.Join(" > ", names);
    }

    /// <summary>Alış fiyatları, fiyat.alis izni olmayan oturum için her zaman maskelenir.</summary>
    public static async Task<ProductDto> GetDtoAsync(IApplicationDbContext db,
                                                     ItemUsage             usage,
                                                     ICurrentSession       session,
                                                     Guid                  id,
                                                     CancellationToken     ct
    ) {
        return ERP.Application.Pricing.PurchaseVisibility.Mask(await ReadDtoAsync(db, usage, id, ct),
                                                              ERP.Application.Pricing.PurchaseVisibility.CanSee(session));
    }

    private static async Task<ProductDto> ReadDtoAsync(IApplicationDbContext db,
                                                       ItemUsage             usage,
                                                       Guid                  id,
                                                       CancellationToken     ct
    ) {
        var p = await db.Products.AsNoTracking().Include(x => x.Variants).Include(x => x.Units)
                    .Include(x => x.Suppliers).Include(x => x.Images)
                    .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException(id.ToString(), "Product");

        var categories = p.CategoryId is null
                             ? new Dictionary<Guid, Category>()
                             : await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var brandName = p.BrandId is null
                            ? null
                            : await db.Brands.AsNoTracking().Where(b => b.Id == p.BrandId).Select(b => b.Name)
                                  .FirstOrDefaultAsync(ct);
        var productBarcode = await db.Barcodes.AsNoTracking()
                                 .Where(b => b.ProductId == id && b.Owner == BarcodeOwner.Product)
                                 .Select(b => b.Value).FirstOrDefaultAsync(ct);
        var accountIds   = p.Suppliers.Select(s => s.CurrentAccountId).ToList();
        var accountNames = await db.CurrentAccounts.AsNoTracking().Where(a => accountIds.Contains(a.Id))
                               .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var componentIds = p.BundleComponents.Select(c => c.ProductId).Distinct().ToList();
        var componentNames = componentIds.Count == 0
                                 ? new Dictionary<Guid, (string Code, string Name)?>()
                                 : (await db.Products.AsNoTracking().Where(x => componentIds.Contains(x.Id))
                                        .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct))
                                 .ToDictionary(x => x.Id, x => ((string Code, string Name)?)(x.Code, x.Name));

        var variants = new List<VariantDto>();
        foreach (var v in p.Variants.OrderBy(v => v.Code, StringComparer.Ordinal))
            variants.Add(new VariantDto(v.Id, v.Code, v.Barcode, v.Values, v.SalePriceOverride, v.PurchasePriceOverride,
                                        v.IsActive, await usage.VariantInUseAsync(v.Id, ct)));

        return new ProductDto(
                              p.Id, p.Kind, p.Code, p.Name, p.Description, p.CategoryId, PathOf(p.CategoryId, categories),
                              p.BrandId, brandName, p.BaseUnit, p.VatRate, p.IsActive, p.SoldByMeasure, p.MeasureUnit,
                              p.TracksSerialLot, productBarcode, p.WeightKg, p.WidthCm, p.LengthCm, p.HeightCm,
                              p.OriginCountry, p.Gtip, p.DefaultPurchasePrice, p.DefaultSalePrice, p.Tags.ToArray(),
                              JsonDocument.Parse(p.CustomFieldsJson).RootElement.Clone(),
                              p.Attributes.OrderBy(a => a.Sort)
                                  .Select(a => new VariantAttributeDto(a.Id, a.Name, a.Values, a.Sort)).ToList(),
                              variants,
                              p.Units.OrderBy(u => u.Unit, StringComparer.Ordinal)
                                  .Select(u => new UnitDto(u.Id, u.Unit, u.Factor, u.Barcode)).ToList(),
                              p.Suppliers.OrderBy(s => s.CurrentAccountId)
                                  .Select(s => new SupplierCodeDto(s.Id, s.CurrentAccountId,
                                                                   accountNames.GetValueOrDefault(s.CurrentAccountId),
                                                                   s.SupplierCode, s.IsDefault)).ToList(),
                              p.Images.OrderBy(i => i.Sort).Select(i => new ImageDto(i.Id, i.FileId, i.Sort)).ToList(),
                              p.TrackingMode, p.IsBundle,
                              p.BundleComponents.Select(c => new BundleComponentDto(c.ProductId, c.VariantId, c.Quantity,
                                                                                    componentNames.GetValueOrDefault(c.ProductId)?.Code,
                                                                                    componentNames.GetValueOrDefault(c.ProductId)?.Name)).ToList(),
                              await usage.ProductInUseAsync(p.Id, ct));
    }
}

[Authorize(Permission = CatalogPermissions.Read)]
public sealed record GetProductQuery(Guid Id) : IRequest<ProductDto>;

public sealed class GetProductQueryHandler(
    IApplicationDbContext db,
    ItemUsage             usage,
    ICurrentSession       session
) : IRequestHandler<GetProductQuery, ProductDto> {
    public Task<ProductDto> Handle(GetProductQuery request, CancellationToken ct) {
        return ProductReader.GetDtoAsync(db, usage, session, request.Id, ct);
    }
}
