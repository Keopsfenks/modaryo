namespace ERP.Domain.Inventory;

public class StockCountLine {
    public Guid     Id           { get; set; } = Guid.CreateVersion7();
    public Guid     StockCountId { get; set; }
    public Guid     ProductId    { get; set; }
    public Guid?    VariantId    { get; set; }

    /// <summary>Sayım anındaki (snapshot) beklenen miktar.</summary>
    public decimal  ExpectedQty  { get; set; }

    /// <summary>Sayılan miktar; null = henüz sayılmadı.</summary>
    public decimal? CountedQty   { get; set; }
}
