namespace ERP.Domain.Pricing;

/// <summary>Müşteriye özel fiyat (en yüksek öncelik).</summary>
public class CustomerPrice : BaseEntity, ITenantOwned {
    public Guid            CurrentAccountId { get; set; }
    public Guid            ProductId        { get; set; }
    public Guid?           VariantId        { get; set; }
    public string          Unit             { get; set; } = string.Empty;
    public string          Currency         { get; set; } = "TRY";
    public bool            VatIncluded      { get; set; }
    public List<PriceTier> Tiers            { get; set; } = new();
    public DateOnly?       ValidFrom        { get; set; }
    public DateOnly?       ValidTo          { get; set; }
    public Guid            TenantId         { get; set; }
}
