using ERP.Domain.Invoicing;

namespace ERP.Domain.Sales;

public class ShipmentLine : BaseEntity, ITenantOwned {
    public Guid     ShipmentId       { get; set; }
    public int      Sort             { get; set; }
    public Guid     SalesOrderLineId { get; set; }
    public Guid     ProductId        { get; set; }
    public Guid?    VariantId        { get; set; }
    public string   Description      { get; set; } = string.Empty;
    public string   Unit             { get; set; } = string.Empty;
    public decimal  Factor           { get; set; } = 1m;
    public decimal  Quantity         { get; set; }
    public decimal? Width            { get; set; }
    public decimal? Length           { get; set; }
    public Guid?    WarehouseId      { get; set; }

    public IReadOnlyList<InvoiceLot>? Lots    { get; set; }
    public IReadOnlyList<string>?     Serials { get; set; }

    /// <summary>Faturalanan miktar.</summary>
    public decimal InvoicedQuantity { get; set; }
    public Guid    TenantId         { get; set; }
}
