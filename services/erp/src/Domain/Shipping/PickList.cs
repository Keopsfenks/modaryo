namespace ERP.Domain.Shipping;

public enum PickListStatus { Open, Completed, Cancelled }

/// <summary>Toplama listesi. Toplu listede birden çok sevk birleşir; satırlar kalem bazında gruplanır.</summary>
public class PickList : BaseAuditableEntity, ITenantOwned {
    public string         Number      { get; set; } = string.Empty;
    public PickListStatus Status      { get; set; } = PickListStatus.Open;
    public Guid           BranchId    { get; set; }
    public Guid           WarehouseId { get; set; }
    public IReadOnlyList<Guid> ShipmentIds { get; set; } = [];
    public List<PickLine> Lines       { get; set; } = new();
    public uint           Version     { get; set; }
    public Guid           TenantId    { get; set; }
}
