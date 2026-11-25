namespace ERP.Domain.Purchasing;

/// <summary>Dağıtım hedefi: bir mal kabul satırı ve ona düşen pay.</summary>
public class LandedCostTarget : BaseEntity, ITenantOwned {
    public Guid     LandedCostId    { get; set; }
    public Guid     GoodsReceiptId  { get; set; }
    public Guid     ReceiptLineId   { get; set; }
    public decimal  TlValue         { get; set; }
    public decimal  BaseQuantity    { get; set; }
    public decimal? WeightKgPerUnit { get; set; }
    public decimal  AllocatedTl     { get; set; }
    public Guid     TenantId        { get; set; }
}
