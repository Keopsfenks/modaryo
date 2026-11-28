namespace ERP.Domain.Pos;

public class PosSaleLine : BaseEntity, ITenantOwned {
    public Guid    SaleId      { get; set; }
    public int     Sort        { get; set; }
    public Guid    ProductId   { get; set; }
    public Guid?   VariantId   { get; set; }
    public string  Description { get; set; } = string.Empty;
    public string  Unit        { get; set; } = string.Empty;
    public decimal Factor      { get; set; } = 1m;
    public decimal Quantity    { get; set; }
    public decimal UnitPrice   { get; set; }
    public int     VatRate     { get; set; }

    /// <summary>Elle verilen satır indirimi.</summary>
    public decimal Discount         { get; set; }
    public decimal CampaignDiscount { get; set; }
    public Guid?   CampaignId       { get; set; }

    public decimal Net   { get; set; }
    public decimal Vat   { get; set; }
    public decimal Total { get; set; }

    /// <summary>Satış anındaki birim maliyet; iade bu maliyetle girer.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>İade edilen miktar (satış birimi cinsinden).</summary>
    public decimal ReturnedQuantity { get; set; }

    public Guid TenantId { get; set; }
}
