namespace ERP.Domain.Inventory;

/// <summary>Geriye dönük hareket sonrası yeniden maliyetlendirme işi; aynı anahtar en erken tarihe birleşir.</summary>
public class RevaluationJob : BaseEntity, ITenantOwned {
    public Guid           WarehouseId { get; set; }
    public Guid           ProductId   { get; set; }
    public Guid?          VariantId   { get; set; }
    public DateOnly       FromDate    { get; set; }
    public DateTimeOffset EnqueuedAt  { get; set; }
    public Guid           TenantId    { get; set; }
}
