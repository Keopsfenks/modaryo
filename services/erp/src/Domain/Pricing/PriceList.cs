namespace ERP.Domain.Pricing;

public class PriceList : BaseAuditableEntity, ITenantOwned {
    public string        Name              { get; set; } = string.Empty;
    public PriceListKind Kind              { get; set; }
    public string        Currency          { get; set; } = "TRY";
    public bool          VatIncluded       { get; set; }
    public DateOnly?     ValidFrom         { get; set; }
    public DateOnly?     ValidTo           { get; set; }
    public bool          IsDefault         { get; set; }

    /// <summary>Tedarikçi cariye bağlı alış listeleri.</summary>
    public Guid?         SupplierId        { get; set; }

    /// <summary>Türetilmiş liste: taban liste + yüzde ayarı.</summary>
    public Guid?         BaseListId        { get; set; }
    public decimal?      AdjustmentPercent { get; set; }
    public bool          IsActive          { get; set; } = true;

    /// <summary>xmin eşzamanlılık belirteci.</summary>
    public uint          Version           { get; set; }
    public Guid          TenantId          { get; set; }
}
