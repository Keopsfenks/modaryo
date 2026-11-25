namespace ERP.Domain.Sales;

public class Quote : BaseAuditableEntity, ITenantOwned {
    public string      Number           { get; set; } = string.Empty;
    public QuoteStatus Status           { get; set; } = QuoteStatus.Draft;
    public Guid        CurrentAccountId { get; set; }
    public Guid        BranchId         { get; set; }
    public string      Currency         { get; set; } = "TRY";
    public Guid?       SalesRepUserId   { get; set; }
    public string?     SalesRepName     { get; set; }

    /// <summary>Geçerli (son) revizyon numarası.</summary>
    public int             CurrentRevision { get; set; } = 1;
    public DateTimeOffset? SentAt          { get; set; }
    public string?         RejectReason    { get; set; }
    public Guid?           SalesOrderId    { get; set; }

    public List<QuoteRevision> Revisions { get; set; } = new();

    /// <summary>xmin eşzamanlılık belirteci.</summary>
    public uint Version  { get; set; }
    public Guid TenantId { get; set; }

    public QuoteRevision Current => Revisions.First(r => r.Revision == CurrentRevision);
}
