using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Catalog;

namespace ERP.Application.Catalog.Items.Queries;

[Authorize(Permission = CatalogPermissions.Read)]
public sealed record LookupBarcodeQuery(string Value) : IRequest<BarcodeHit>;

/// <summary>
///     Barkoddan kalemi bulur. Varyant barkodu o varyantı, birim barkodu birimi ve katsayısıyla, ürün barkodu temel
///     birimi döndürür. Varyantlı kartın kart düzeyindeki barkodları satılabilir kalem vermez (404). Pasif kalemler
///     IsActive=false olarak döner; kararı çağıran modül verir.
/// </summary>
public sealed class LookupBarcodeQueryHandler(
    IApplicationDbContext db,
    ICurrentSession       session
) : IRequestHandler<LookupBarcodeQuery, BarcodeHit> {
    public async Task<BarcodeHit> Handle(LookupBarcodeQuery request, CancellationToken ct) {
        return ERP.Application.Pricing.PurchaseVisibility.Mask(await BarcodeResolver.FindAsync(db, request.Value, ct),
                                                              ERP.Application.Pricing.PurchaseVisibility.CanSee(session));
    }
}

/// <summary>Barkod çözümü (katalog sorgusu ve POS sorgusu ortak kullanır; izin denetimi çağıranındır).</summary>
public static class BarcodeResolver {
    public static async Task<BarcodeHit> FindAsync(IApplicationDbContext db, string raw, CancellationToken ct) {
        var value = raw.Trim();
        var row = await db.Barcodes.AsNoTracking().FirstOrDefaultAsync(b => b.Value == value, ct)
                  ?? throw new NotFoundException(value, "Barcode");

        var product = await db.Products.AsNoTracking().Include(p => p.Variants).Include(p => p.Units)
                          .FirstOrDefaultAsync(p => p.Id == row.ProductId, ct)
                      ?? throw new NotFoundException(value, "Barcode");

        switch (row.Owner) {
            case BarcodeOwner.Variant: {
                var variant = product.Variants.FirstOrDefault(v => v.Id == row.OwnerId)
                              ?? throw new NotFoundException(value, "Barcode");
                return new BarcodeHit(SellableItem.From(product, variant), product.BaseUnit, 1m);
            }
            case BarcodeOwner.Unit when product.Variants.Count == 0: {
                var unit = product.Units.FirstOrDefault(u => u.Id == row.OwnerId)
                           ?? throw new NotFoundException(value, "Barcode");
                return new BarcodeHit(SellableItem.From(product, null), unit.Unit, unit.Factor);
            }
            case BarcodeOwner.Product when product.Variants.Count == 0:
                return new BarcodeHit(SellableItem.From(product, null), product.BaseUnit, 1m);
            default:
                throw new NotFoundException(value, "Barcode");
        }
    }
}
