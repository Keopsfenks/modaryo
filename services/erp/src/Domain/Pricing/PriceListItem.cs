namespace ERP.Domain.Pricing;

public class PriceListItem : BaseEntity, ITenantOwned {
    public Guid            PriceListId { get; set; }
    public Guid            ProductId   { get; set; }
    public Guid?           VariantId   { get; set; }
    public string          Unit        { get; set; } = string.Empty;

    /// <summary>jsonb.</summary>
    public List<PriceTier> Tiers       { get; set; } = new();

    /// <summary>xmin eşzamanlılık belirteci.</summary>
    public uint            Version     { get; set; }
    public Guid            TenantId    { get; set; }
}
