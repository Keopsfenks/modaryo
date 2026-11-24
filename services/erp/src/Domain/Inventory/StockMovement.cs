namespace ERP.Domain.Inventory;

public class MovementLotLine {
    public Guid    Id         { get; set; } = Guid.CreateVersion7();
    public Guid    MovementId { get; set; }
    public Guid?   LotId      { get; set; }
    public Guid?   SerialId   { get; set; }
    public decimal Quantity   { get; set; }
}

public class StockMovement : BaseEntity, ITenantOwned {
    /// <summary>bigint identity; aynı gün içinde toplam sıralama.</summary>
    public long           Sequence     { get; set; }
    public Guid           WarehouseId  { get; set; }
    public Guid           ProductId    { get; set; }
    public Guid?          VariantId    { get; set; }
    public MovementType   Type         { get; set; }
    public DateOnly       Date         { get; set; }

    /// <summary>İşaretli miktar, ürünün temel biriminde.</summary>
    public decimal        Quantity     { get; set; }
    public decimal        UnitCost     { get; set; }

    /// <summary>İşaretli toplam maliyet.</summary>
    public decimal        TotalCost    { get; set; }
    public string         SourceType   { get; set; } = string.Empty;
    public Guid           SourceId     { get; set; }
    public Guid?          SourceLineId { get; set; }
    public string?        Note         { get; set; }
    public DateTimeOffset CreatedAt    { get; set; }
    public string         ActorLabel   { get; set; } = string.Empty;

    public List<MovementLotLine> LotLines { get; set; } = new();

    public Guid TenantId { get; set; }
}
