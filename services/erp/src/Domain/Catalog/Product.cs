using ERP.Domain.Inventory;

namespace ERP.Domain.Catalog;

public class Product : BaseAuditableEntity, ITenantOwned {
    public ProductKind  Kind                 { get; set; }
    public string       Code                 { get; set; } = string.Empty;
    public string       Name                 { get; set; } = string.Empty;
    public string?      Description          { get; set; }
    public Guid?        CategoryId           { get; set; }
    public Guid?        BrandId              { get; set; }
    public string       BaseUnit             { get; set; } = "adet";
    public int          VatRate              { get; set; } = 20;
    public bool         IsActive             { get; set; } = true;
    public bool         SoldByMeasure        { get; set; }
    public string?      MeasureUnit          { get; set; }
    public bool         TracksSerialLot      { get; set; }
    public TrackingMode? TrackingMode        { get; set; }
    public bool         IsBundle             { get; set; }
    public decimal?     WeightKg             { get; set; }
    public decimal?     WidthCm              { get; set; }
    public decimal?     LengthCm             { get; set; }
    public decimal?     HeightCm             { get; set; }
    public string?      OriginCountry        { get; set; }
    public string?      Gtip                 { get; set; }
    public Money4?      DefaultPurchasePrice { get; set; }
    public Money4?      DefaultSalePrice     { get; set; }
    public List<string> Tags                 { get; set; } = new();
    public string       CustomFieldsJson     { get; set; } = "{}";

    public List<VariantAttribute> Attributes { get; set; } = new();
    public List<ProductVariant>   Variants   { get; set; } = new();
    public List<ProductUnit>      Units      { get; set; } = new();
    public List<ProductSupplier>  Suppliers  { get; set; } = new();
    public List<ProductImage>     Images     { get; set; } = new();

    /// <summary>Set ürün bileşenleri (iç içe set yok).</summary>
    public List<BundleComponent>  BundleComponents { get; set; } = new();

    public Guid TenantId { get; set; }
}
