namespace ERP.Domain.Treasury;

/// <summary>Mahsup fişi; kaynak elle girilen, belge veya dış servis (hr, assets) olabilir. Toplam borç = toplam alacak.</summary>
public class JournalVoucher : BaseAuditableEntity, ITenantOwned {
    public string            Number         { get; set; } = string.Empty;
    public DateOnly          Date           { get; set; }
    public string            Description    { get; set; } = string.Empty;
    public string            SourceType     { get; set; } = "Manual";
    public Guid?             SourceId       { get; set; }
    public string?           ExternalCaller { get; set; }
    public List<JournalLine> Lines          { get; set; } = new();
    public bool              IsCancelled    { get; set; }
    public DateTimeOffset?   CancelledAt    { get; set; }
    public string?           CancelReason   { get; set; }
    public Guid              TenantId       { get; set; }

    public bool IsBalanced => Lines.Sum(l => l.Debit) == Lines.Sum(l => l.Credit);
}
