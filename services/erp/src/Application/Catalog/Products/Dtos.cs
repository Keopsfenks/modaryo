using System.Text.Json;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;

namespace ERP.Application.Catalog.Products;

public sealed record VariantAttributeDto(Guid Id, string Name, IReadOnlyList<string> Values, int Sort);

public sealed record VariantDto(
    Guid                       Id,
    string                     Code,
    string?                    Barcode,
    IReadOnlyDictionary<string, string> Values,
    Money4?                    SalePriceOverride,
    Money4?                    PurchasePriceOverride,
    bool                       IsActive,
    bool                       InUse
);

public sealed record UnitDto(Guid Id, string Unit, decimal Factor, string? Barcode);

public sealed record SupplierCodeDto(
    Guid    Id,
    Guid    CurrentAccountId,
    string? CurrentAccountName,
    string? SupplierCode,
    bool    IsDefault
);

public sealed record ImageDto(Guid Id, Guid FileId, int Sort);

public sealed record BundleComponentDto(
    Guid    ProductId,
    Guid?   VariantId,
    decimal Quantity,
    string? ProductCode,
    string? ProductName
);

public sealed record ProductDto(
    Guid                              Id,
    ProductKind                       Kind,
    string                            Code,
    string                            Name,
    string?                           Description,
    Guid?                             CategoryId,
    string?                           CategoryPath,
    Guid?                             BrandId,
    string?                           BrandName,
    string                            BaseUnit,
    int                               VatRate,
    bool                              IsActive,
    bool                              SoldByMeasure,
    string?                           MeasureUnit,
    bool                              TracksSerialLot,
    string?                           Barcode,
    decimal?                          WeightKg,
    decimal?                          WidthCm,
    decimal?                          LengthCm,
    decimal?                          HeightCm,
    string?                           OriginCountry,
    string?                           Gtip,
    Money4?                           DefaultPurchasePrice,
    Money4?                           DefaultSalePrice,
    string[]                          Tags,
    JsonElement                       CustomFields,
    IReadOnlyList<VariantAttributeDto> Attributes,
    IReadOnlyList<VariantDto>         Variants,
    IReadOnlyList<UnitDto>            Units,
    IReadOnlyList<SupplierCodeDto>    Suppliers,
    IReadOnlyList<ImageDto>           Images,
    TrackingMode?                     TrackingMode,
    bool                              IsBundle,
    IReadOnlyList<BundleComponentDto> BundleComponents,
    bool                              InUse
);

public sealed record ProductListItem(
    Guid        Id,
    ProductKind Kind,
    string      Code,
    string      Name,
    Guid?       CategoryId,
    string?     CategoryPath,
    Guid?       BrandId,
    string?     BrandName,
    string      BaseUnit,
    int         VatRate,
    bool        IsActive,
    string?     Barcode,
    Money4?     DefaultSalePrice,
    int         VariantCount,
    string[]    Tags,
    Guid?       FirstImageFileId
);

public sealed record NextCodeResult(string Code);

public sealed record BarcodeResult(string Barcode);

// --- Girdiler ---

public sealed record AttributeInput(string Name, List<string>? Values);

public sealed record VariantInput(
    string?                     Code,
    string?                     Barcode,
    Dictionary<string, string>? Values,
    Money4?                     SalePriceOverride,
    Money4?                     PurchasePriceOverride,
    bool?                       IsActive
);

public sealed record UnitInput(string Unit, decimal Factor, string? Barcode);

public sealed record SupplierInput(Guid CurrentAccountId, string? SupplierCode, bool IsDefault);

public sealed record ImageInput(Guid FileId);

public sealed record BundleComponentInput(Guid ProductId, Guid? VariantId, decimal Quantity);

public abstract record ProductInput {
    public ProductKind Kind { get; init; }

    /// <summary>Boşsa ayarlardaki şablondan sıradaki kod verilir.</summary>
    public string? Code { get; init; }

    public string  Name        { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid?   CategoryId  { get; init; }
    public Guid?   BrandId     { get; init; }

    /// <summary>Boşsa "adet" (hizmette ayarlardaki ilk birim).</summary>
    public string? BaseUnit { get; init; }

    /// <summary>Boşsa ayarlardaki varsayılan KDV.</summary>
    public int? VatRate { get; init; }

    public bool    SoldByMeasure   { get; init; }
    public string? MeasureUnit     { get; init; }
    public bool    TracksSerialLot { get; init; }

    /// <summary>Seri/lot takibi açıkken Lot | Serial; boşsa mevcut değer, o da yoksa Lot.</summary>
    public TrackingMode? TrackingMode { get; init; }

    /// <summary>Set ürün: kendi stoğu yoktur, bileşenlerinden oluşur.</summary>
    public bool IsBundle { get; init; }

    public List<BundleComponentInput>? BundleComponents { get; init; }

    public string? Barcode         { get; init; }
    public decimal? WeightKg       { get; init; }
    public decimal? WidthCm        { get; init; }
    public decimal? LengthCm       { get; init; }
    public decimal? HeightCm       { get; init; }
    public string?  OriginCountry  { get; init; }
    public string?  Gtip           { get; init; }
    public Money4?  DefaultPurchasePrice { get; init; }
    public Money4?  DefaultSalePrice     { get; init; }
    public List<string>? Tags { get; init; }
    public JsonElement?  CustomFields { get; init; }

    /// <summary>Yalnızca oluştururken kullanılır; sonradan /attributes ve /variants uçlarıyla yönetilir.</summary>
    public List<AttributeInput>? Attributes { get; init; }

    /// <summary>Yalnızca oluştururken kullanılır. Boşsa özelliklerden tüm kombinasyonlar üretilir.</summary>
    public List<VariantInput>? Variants { get; init; }

    public List<UnitInput>?     Units     { get; init; }
    public List<SupplierInput>? Suppliers { get; init; }
    public List<ImageInput>?    Images    { get; init; }
}
