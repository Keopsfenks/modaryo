namespace ERP.Domain.Shipping;

/// <summary>Önerilen/toplanan lot (FEFO). Alan adları fatura lotuyla uyumludur.</summary>
public sealed record PickLot(string LotNo, DateOnly? Expiry, decimal Quantity, Guid? LotId);

/// <summary>Satırın hangi sevk satırlarını karşıladığı (toplu listede birden çok).</summary>
public sealed record PickSource(Guid ShipmentLineId, decimal Quantity);

public class PickLine : BaseEntity, ITenantOwned {
    public Guid     PickListId  { get; set; }
    public int      Sort        { get; set; }
    public Guid     WarehouseId { get; set; }
    public Guid     ProductId   { get; set; }
    public Guid?    VariantId   { get; set; }
    public string   Description { get; set; } = string.Empty;
    public string   Unit        { get; set; } = string.Empty;

    /// <summary>Birim çarpanı (stok birimi = miktar x çarpan); lot miktarları stok biriminde tutulur.</summary>
    public decimal  Factor      { get; set; } = 1m;

    /// <summary>İstenen miktar (satış birimi).</summary>
    public decimal  Quantity    { get; set; }

    /// <summary>Toplanan miktar; null = henüz işlenmedi.</summary>
    public decimal? PickedQuantity { get; set; }

    /// <summary>WMS kapalıyken yalnızca depo; WMS açıkken raf konumu.</summary>
    public string?  Location    { get; set; }

    public IReadOnlyList<PickLot>?    SuggestedLots { get; set; }
    public IReadOnlyList<PickLot>?    PickedLots    { get; set; }
    public IReadOnlyList<string>?     PickedSerials { get; set; }
    public IReadOnlyList<PickSource>? Sources       { get; set; }
    public Guid     TenantId    { get; set; }
}
