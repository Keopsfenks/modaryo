namespace ERP.Domain.Pricing;

public class PriceHistoryEntry : BaseEntity, ITenantOwned {
    public DateTimeOffset At         { get; set; }
    public string         ActorLabel { get; set; } = string.Empty;

    /// <summary>list | customer | card</summary>
    public string         Scope      { get; set; } = string.Empty;
    public Guid           ScopeId    { get; set; }
    public Guid           ProductId  { get; set; }
    public Guid?          VariantId  { get; set; }
    public string         Unit       { get; set; } = string.Empty;
    public string?        BeforeJson { get; set; }
    public string?        AfterJson  { get; set; }
    public PriceListKind  Kind       { get; set; }
    public Guid           TenantId   { get; set; }
}
