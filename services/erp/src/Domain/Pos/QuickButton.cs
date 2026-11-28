namespace ERP.Domain.Pos;

/// <summary>Hızlı satış tuşu. Sayfa başına en fazla 6x6, en fazla 10 sayfa.</summary>
public class QuickButton : BaseEntity, ITenantOwned {
    public const int MaxPages = 10;
    public const int MaxRows  = 6;
    public const int MaxCols  = 6;

    public Guid     RegisterId { get; set; }
    public int      Page       { get; set; }
    public int      Row        { get; set; }
    public int      Col        { get; set; }
    public Guid     ProductId  { get; set; }
    public Guid?    VariantId  { get; set; }
    public decimal? PresetQty  { get; set; }
    public string   Color      { get; set; } = string.Empty;
    public Guid     TenantId   { get; set; }
}
