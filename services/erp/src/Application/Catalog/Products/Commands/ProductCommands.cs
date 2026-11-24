using ERP.Application.Catalog.Products.Queries;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;

namespace ERP.Application.Catalog.Products.Commands;

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record CreateProductCommand : ProductInput, IRequest<ProductDto>;

public sealed class CreateProductCommandHandler(
    ProductWriter         writer,
    IApplicationDbContext db,
    ItemUsage             usage,
    ICurrentSession       session
) : IRequestHandler<CreateProductCommand, ProductDto> {
    public async Task<ProductDto> Handle(CreateProductCommand request, CancellationToken ct) {
        var id = await writer.SaveAsync(null, request, ct);
        return await ProductReader.GetDtoAsync(db, usage, session, id, ct);
    }
}

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record UpdateProductCommand : ProductInput, IRequest<ProductDto> {
    public Guid Id { get; init; }
}

public sealed class UpdateProductCommandHandler(
    ProductWriter         writer,
    IApplicationDbContext db,
    ItemUsage             usage,
    ICurrentSession       session
) : IRequestHandler<UpdateProductCommand, ProductDto> {
    public async Task<ProductDto> Handle(UpdateProductCommand request, CancellationToken ct) {
        var product = await db.Products.Include(p => p.Variants).Include(p => p.Units).Include(p => p.Suppliers)
                          .Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == request.Id, ct)
                      ?? throw new NotFoundException(request.Id.ToString(), "Product");

        await writer.SaveAsync(product, request, ct);
        return await ProductReader.GetDtoAsync(db, usage, session, product.Id, ct);
    }
}

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record SetProductActiveCommand(Guid Id, bool Active) : IRequest;

public sealed class SetProductActiveCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<SetProductActiveCommand> {
    public async Task Handle(SetProductActiveCommand request, CancellationToken ct) {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == request.Id, ct)
                      ?? throw new NotFoundException(request.Id.ToString(), "Product");
        product.IsActive = request.Active;
        await db.SaveChangesAsync(ct);
    }
}

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record DeleteProductCommand(Guid Id) : IRequest;

public sealed class DeleteProductCommandHandler(
    IApplicationDbContext db,
    ItemUsage             usage
) : IRequestHandler<DeleteProductCommand> {
    public async Task Handle(DeleteProductCommand request, CancellationToken ct) {
        var product = await db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == request.Id, ct)
                      ?? throw new NotFoundException(request.Id.ToString(), "Product");

        if (await usage.ProductInUseAsync(product.Id, ct)) throw CatalogErrors.ProductInUse();
        foreach (var variant in product.Variants)
            if (await usage.VariantInUseAsync(variant.Id, ct))
                throw CatalogErrors.ProductInUse();

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
    }
}

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record GenerateBarcodeCommand(Guid ProductId) : IRequest<BarcodeResult>;

/// <summary>Ayarlardaki önek + sayaçtan EAN-13 üretir; kartı değiştirmez, kullanılmayan bir barkod döndürür.</summary>
public sealed class GenerateBarcodeCommandHandler(
    IApplicationDbContext db,
    ITenantSettings       settingsService,
    BarcodeRegistry       barcodes
) : IRequestHandler<GenerateBarcodeCommand, BarcodeResult> {
    private const string Field = "nextBarcodeSequence";

    public async Task<BarcodeResult> Handle(GenerateBarcodeCommand request, CancellationToken ct) {
        if (!await db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
            throw new NotFoundException(request.ProductId.ToString(), "Product");

        var settings = await settingsService.GetAsync<CatalogSettings>(ct);
        for (var attempt = 0; attempt < 100; attempt++) {
            var seq = await db.ReserveCatalogSequenceAsync(Field, 1, ProductCodes.SeedJson(Field, 2), ct);
            if (seq > 99999)
                throw new AppException(409, "barcode_sequence_exhausted",
                                       "Barkod sırası tükendi. Ayarlardan barkod önekini değiştirin.");

            var barcode = Ean13.Build(settings.BarcodePrefix, seq);
            if (!await barcodes.IsTakenAsync(barcode, ct)) return new BarcodeResult(barcode);
        }

        throw new AppException(409, "barcode_sequence_exhausted", "Boş barkod bulunamadı.");
    }
}
