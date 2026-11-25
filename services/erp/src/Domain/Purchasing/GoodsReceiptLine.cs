namespace ERP.Domain.Purchasing;

public class GoodsReceiptLine : BaseEntity, ITenantOwned {
    public Guid   GoodsReceiptId       { get; set; }
    public Guid?  PurchaseOrderLineId  { get; set; }
    public int    Sort                 { get; set; }
    public Guid   ProductId            { get; set; }
    public Guid?  VariantId            { get; set; }
    public string Unit                 { get; set; } = string.Empty;
    public decimal Quantity            { get; set; }
    public decimal BaseQuantity        { get; set; }

    /// <summary>Belge para biriminde, satır birimi başına fiyat (fiyat yetkisi yoksa girilmez).</summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>Mal kabul kuruyla TL, temel birim başına maliyet.</summary>
    public decimal? UnitCostTl { get; set; }

    /// <summary>Maliyet düzeltmeleri ve masraf dağıtımı sonrası temel birim başına TL maliyet.</summary>
    public decimal? AdjustedUnitCostTl { get; set; }

    /// <summary>Satırın girdiği depo (karantinadaysa karantina deposu).</summary>
    public Guid            WarehouseId       { get; set; }
    public QuarantineState Quarantine        { get; set; } = QuarantineState.None;
    public Guid?           TargetWarehouseId { get; set; }

    public Guid TenantId { get; set; }
}
