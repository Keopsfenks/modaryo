namespace ERP.Domain.Inventory;

public enum StockDocumentKind { Receipt, Issue, Opening }

public enum StockDocumentStatus { Active, Cancelled }

public class StockDocument : BaseEntity, ITenantOwned {
    public string              Number       { get; set; } = string.Empty;
    public StockDocumentKind   Kind         { get; set; }
    public StockDocumentStatus Status       { get; set; } = StockDocumentStatus.Active;
    public DateOnly            Date         { get; set; }
    public Guid                WarehouseId  { get; set; }
    public string?             Reason       { get; set; }
    public string?             CancelReason { get; set; }
    public DateTimeOffset      CreatedAt    { get; set; }
    public DateTimeOffset?     CancelledAt  { get; set; }
    public string              CreatedBy    { get; set; } = string.Empty;

    public List<StockDocumentLine> Lines { get; set; } = new();

    public Guid TenantId { get; set; }
}

public class StockDocumentLine {
    public Guid     Id           { get; set; } = Guid.CreateVersion7();
    public Guid     DocumentId   { get; set; }
    public Guid     ProductId    { get; set; }
    public Guid?    VariantId    { get; set; }
    public string   Unit         { get; set; } = string.Empty;
    public decimal  Quantity     { get; set; }
    public decimal  BaseQuantity { get; set; }

    /// <summary>Temel birim başına maliyet (satırda girilen birim maliyetten çevrilmiş).</summary>
    public decimal? UnitCost     { get; set; }

    /// <summary>Lot/seri girişi JSON olarak saklanır (görüntüleme için).</summary>
    public string?  TrackingJson { get; set; }
}
