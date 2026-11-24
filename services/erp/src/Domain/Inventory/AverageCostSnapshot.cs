namespace ERP.Domain.Inventory;

/// <summary>Depo x kalem için güncel miktar ve ağırlıklı ortalama maliyet.</summary>
public class AverageCostSnapshot : BaseEntity, ITenantOwned {
    public Guid    WarehouseId { get; set; }
    public Guid    ProductId   { get; set; }
    public Guid?   VariantId   { get; set; }
    public decimal Quantity    { get; set; }
    public decimal AverageCost { get; set; }
    public Guid    TenantId    { get; set; }
}
