namespace ERP.Domain.Catalog;

/// <summary>Firma genelinde barkod tekilliği için kayıt defteri. Birleşik anahtar (TenantId, Value).</summary>
public class Barcode : ITenantOwned {
    public Guid         TenantId  { get; set; }
    public string       Value     { get; set; } = string.Empty;
    public BarcodeOwner Owner     { get; set; }
    public Guid         OwnerId   { get; set; }
    public Guid         ProductId { get; set; }
}
