namespace ERP.Domain.Purchasing;

/// <summary>Masraf kaynağı: kesilmiş hizmet alış faturası ya da TL cinsinden elle girilen tutar.</summary>
public class LandedCostSource : BaseEntity, ITenantOwned {
    public Guid    LandedCostId { get; set; }
    public Guid?   InvoiceId    { get; set; }
    public string  Description  { get; set; } = string.Empty;

    /// <summary>Masraf türü (nakliye, gümrük, sigorta ...).</summary>
    public string? CostType { get; set; }
    public decimal AmountTl { get; set; }
    public Guid    TenantId { get; set; }
}
