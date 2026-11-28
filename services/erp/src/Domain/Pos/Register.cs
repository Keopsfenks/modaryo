namespace ERP.Domain.Pos;

/// <summary>Kasa noktası: şube, depo, nakit ve kart hesabı ile hızlı tuş yerleşimi.</summary>
public class Register : BaseAuditableEntity, ITenantOwned {
    public string Code          { get; set; } = string.Empty;
    public string Name          { get; set; } = string.Empty;
    public Guid   BranchId      { get; set; }
    public Guid   WarehouseId   { get; set; }
    public Guid   CashAccountId { get; set; }
    public Guid   CardAccountId { get; set; }
    public bool   IsActive      { get; set; } = true;

    public List<QuickButton> Buttons { get; set; } = new();

    public uint Version  { get; set; }
    public Guid TenantId { get; set; }
}
