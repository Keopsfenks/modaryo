namespace ERP.Domain.Invoicing;

/// <summary>Faturanın vade planı satırı (tutar carinin para biriminde; toplamı defter tutarına eşittir).</summary>
public class InvoiceDueLine : BaseEntity, ITenantOwned {
    public Guid     InvoiceId { get; set; }
    public int      Sort      { get; set; }
    public DateOnly DueDate   { get; set; }
    public decimal  Amount    { get; set; }
    public Guid     TenantId  { get; set; }
}
