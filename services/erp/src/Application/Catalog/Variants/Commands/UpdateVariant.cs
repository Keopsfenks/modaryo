using ERP.Application.Catalog.Products;
using ERP.Application.Catalog.Products.Queries;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Catalog;
using FluentValidation.Results;

namespace ERP.Application.Catalog.Variants.Commands;

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record UpdateVariantCommand : IRequest<ProductDto> {
    public Guid     ProductId             { get; init; }
    public Guid     VariantId             { get; init; }
    public string?  Code                  { get; init; }
    public string?  Barcode               { get; init; }
    public Money4?  SalePriceOverride     { get; init; }
    public Money4?  PurchasePriceOverride { get; init; }
}

public sealed class UpdateVariantCommandHandler(
    IApplicationDbContext db,
    ItemUsage             usage,
    BarcodeRegistry       barcodes,
    ICurrentSession       session,
    ERP.Application.Pricing.PriceHistoryWriter priceHistory
) : IRequestHandler<UpdateVariantCommand, ProductDto> {
    public async Task<ProductDto> Handle(UpdateVariantCommand request, CancellationToken ct) {
        var product = await db.Products.Include(p => p.Variants).Include(p => p.Units)
                          .FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
                      ?? throw new NotFoundException(request.ProductId.ToString(), "Product");
        var variant = product.Variants.FirstOrDefault(v => v.Id == request.VariantId)
                      ?? throw new NotFoundException(request.VariantId.ToString(), "ProductVariant");

        var failures = new List<ValidationFailure>();
        var code     = (request.Code ?? string.Empty).Trim();
        if (code.Length is < 1 or > 40) failures.Add(new ValidationFailure("Code", "Varyant kodu 1 ile 40 karakter arasında olmalıdır."));

        var barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
        if (barcode is { Length: > 40 }) failures.Add(new ValidationFailure("Barcode", "Barkod en fazla 40 karakter olabilir."));

        ProductWriter.CheckMoney(request.SalePriceOverride, "SalePriceOverride",
                                 (field, message) => failures.Add(new ValidationFailure(field, message)));
        ProductWriter.CheckMoney(request.PurchasePriceOverride, "PurchasePriceOverride",
                                 (field, message) => failures.Add(new ValidationFailure(field, message)));
        if (failures.Count > 0) throw new Common.Exceptions.ValidationException(failures);

        if (code != variant.Code
            && (await db.Products.AnyAsync(p => p.Code == code, ct)
                || await db.ProductVariants.AnyAsync(v => v.Code == code && v.Id != variant.Id, ct)))
            throw CatalogErrors.CodeTaken(code);

        // Varyant fiyat geçersiz kılmaları kartın etkin fiyatını değiştirir; geçmişe aynı işlemde yazılır.
        if (variant.SalePriceOverride != request.SalePriceOverride)
            priceHistory.Record(ERP.Application.Pricing.PriceHistoryWriter.ScopeCard, product.Id, product.Id, variant.Id, product.BaseUnit,
                                variant.SalePriceOverride, request.SalePriceOverride, ERP.Domain.Pricing.PriceListKind.Sale);
        // fiyat.alis izni yoksa alış fiyatı gönderilse de mevcut değer korunur.
        var canSeePurchase = ERP.Application.Pricing.PurchaseVisibility.CanSee(session);
        if (canSeePurchase && variant.PurchasePriceOverride != request.PurchasePriceOverride)
            priceHistory.Record(ERP.Application.Pricing.PriceHistoryWriter.ScopeCard, product.Id, product.Id, variant.Id, product.BaseUnit,
                                variant.PurchasePriceOverride, request.PurchasePriceOverride, ERP.Domain.Pricing.PriceListKind.Purchase);

        variant.Code                  = code;
        variant.SalePriceOverride     = request.SalePriceOverride;
        if (canSeePurchase) variant.PurchasePriceOverride = request.PurchasePriceOverride;
        variant.Barcode               = barcode;

        // Kartın kendi barkodları (ürün, diğer varyantlar, birimler) ile yinelenme ve başka kartlarla çakışma.
        var productBarcode = await db.Barcodes.Where(b => b.ProductId == product.Id && b.Owner == BarcodeOwner.Product)
                                 .Select(b => b.Value).FirstOrDefaultAsync(ct);
        var all = new List<string>();
        if (productBarcode is not null) all.Add(productBarcode);
        all.AddRange(product.Variants.Where(v => v.Barcode is not null).Select(v => v.Barcode!));
        all.AddRange(product.Units.Where(u => u.Barcode is not null).Select(u => u.Barcode!));
        await barcodes.EnsureFreeAsync(all, product.Id, ct);
        await barcodes.SyncAsync(product, productBarcode, ct);

        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (CatalogErrors.FromUniqueViolation(ex) is { } conflict) {
            throw conflict;
        }

        return await ProductReader.GetDtoAsync(db, usage, session, product.Id, ct);
    }
}
