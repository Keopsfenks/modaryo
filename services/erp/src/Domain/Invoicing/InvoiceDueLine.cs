namespace ERP.Domain.Invoicing;

/// <summary>Faturanın vade planı satırı (belge para biriminde tutar).</summary>
public class InvoiceDueLine : BaseEntity, ITenantOwned {
    public Guid     InvoiceId { get; set; }
    public int      Sort      { get; set; }
    public DateOnly DueDate   { get; set; }
    public decimal  Amount    { get; set; }
    public Guid     TenantId  { get; set; }
}
