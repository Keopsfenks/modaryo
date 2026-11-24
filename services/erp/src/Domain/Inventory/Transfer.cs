namespace ERP.Domain.Inventory;

public class Transfer : BaseEntity, ITenantOwned {
    public string         Number          { get; set; } = string.Empty;
    public DateOnly       Date            { get; set; }
    public Guid           FromWarehouseId { get; set; }
    public Guid           ToWarehouseId   { get; set; }
    public string?        Note            { get; set; }
    public DateTimeOffset CreatedAt       { get; set; }
    public string         CreatedBy       { get; set; } = string.Empty;

    public List<TransferLineEntry> Lines { get; set; } = new();

    public Guid TenantId { get; set; }
}

public class TransferLineEntry {
    public Guid    Id           { get; set; } = Guid.CreateVersion7();
    public Guid    TransferId   { get; set; }
    public Guid    ProductId    { get; set; }
    public Guid?   VariantId    { get; set; }
    public string  Unit         { get; set; } = string.Empty;
    public decimal Quantity     { get; set; }
    public decimal BaseQuantity { get; set; }
}

/// <summary>Belge numarası sayacı: (firma, anahtar) başına tek satır; örn. "SF2026".</summary>
public class DocumentCounter {
    public Guid   TenantId { get; set; }
    public string Key      { get; set; } = string.Empty;
    public int    Value    { get; set; }
}
