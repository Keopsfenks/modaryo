namespace ERP.Domain.Purchasing;

public class LandedCost : BaseAuditableEntity, ITenantOwned {
    public string           Number       { get; set; } = string.Empty;
    public LandedCostStatus Status       { get; set; } = LandedCostStatus.Draft;
    public DateOnly         Date         { get; set; }
    public AllocationKey    Key          { get; set; } = AllocationKey.Amount;
    public Guid?            ImportFileId { get; set; }
    public string?          Notes        { get; set; }
    public string?          CancelReason { get; set; }
    public decimal          TotalTl      { get; set; }

    public List<LandedCostSource> Sources { get; set; } = new();
    public List<LandedCostTarget> Targets { get; set; } = new();

    public Guid TenantId { get; set; }
}
