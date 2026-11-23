namespace ERP.Domain.Pricing;

public class Campaign : BaseAuditableEntity, ITenantOwned {
    public string       Name        { get; set; } = string.Empty;
    public CampaignKind Kind        { get; set; }
    public int?         BuyQty      { get; set; }
    public int?         PayQty      { get; set; }
    public decimal?     Percent     { get; set; }
    public DateOnly?    ValidFrom   { get; set; }
    public DateOnly?    ValidTo     { get; set; }

    /// <summary>Zaman penceresi (İstanbul saati); null/boş = her gün.</summary>
    public DayOfWeek[]? Days        { get; set; }
    public TimeOnly?    FromTime    { get; set; }
    public TimeOnly?    ToTime      { get; set; }
    public Guid[]       ProductIds  { get; set; } = [];
    public Guid[]       VariantIds  { get; set; } = [];
    public Guid[]       CategoryIds { get; set; } = [];
    public bool         IsActive    { get; set; } = true;
    public Guid         TenantId    { get; set; }
}
