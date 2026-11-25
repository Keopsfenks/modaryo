namespace ERP.Domain.Treasury;

/// <summary>
///     İşlenmiş kur farkının fatura hareketi (defter hareketi) bazındaki payı. Kimlik tahsis kimliği değil faturanın defter hareketidir:
///     tahsisler yeniden kurulduğunda (iptal, FIFO kayması) değişirler, fatura hareketi değişmez. Tutar işlenen TL farktır (işaretli).
/// </summary>
public class ExchangeDifferenceSettlementItem : BaseEntity, ITenantOwned {
    public Guid    SettlementId   { get; set; }
    public Guid    InvoiceEntryId { get; set; }
    public decimal Amount         { get; set; }
    public Guid    TenantId       { get; set; }
}
