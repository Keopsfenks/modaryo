namespace ERP.Domain.Treasury;

/// <summary>Çek/senet durum geçmişi satırı.</summary>
public class InstrumentEvent : BaseEntity, ITenantOwned {
    public Guid              InstrumentId { get; set; }
    public Guid?             DocumentId   { get; set; }
    public InstrumentStatus? From         { get; set; }
    public InstrumentStatus  To           { get; set; }
    public DateOnly          Date         { get; set; }
    public string?           Note         { get; set; }
    public DateTimeOffset    Created      { get; set; }
    public string?           CreatedBy    { get; set; }
    public Guid              TenantId     { get; set; }
}
