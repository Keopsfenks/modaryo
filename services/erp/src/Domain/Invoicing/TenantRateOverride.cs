namespace ERP.Domain.Invoicing;

/// <summary>Firmanın bir gün/para birimi/kur türü için TCMB kurunun yerine kullandığı elle kur.</summary>
public class TenantRateOverride : BaseEntity, ITenantOwned {
    public DateOnly Date       { get; set; }
    public string   Currency   { get; set; } = string.Empty;
    public RateType Type       { get; set; }
    public decimal  Rate       { get; set; }
    public string   ActorLabel { get; set; } = string.Empty;
    public Guid     TenantId   { get; set; }
}
