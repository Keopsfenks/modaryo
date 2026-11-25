using ERP.Domain.Inventory;

namespace ERP.Domain.Sales;

public class SalesReturn : BaseAuditableEntity, ITenantOwned {
    public string            Number           { get; set; } = string.Empty;
    public SalesReturnStatus Status           { get; set; } = SalesReturnStatus.Draft;
    public Guid              InvoiceId        { get; set; }
    public Guid?             SalesOrderId     { get; set; }
    public Guid              CurrentAccountId { get; set; }
    public Guid              BranchId         { get; set; }
    public DateOnly          Date             { get; set; }
    public Guid              WarehouseId      { get; set; }
    public WarehouseKind     WarehouseKind    { get; set; } = WarehouseKind.Normal;
    public string?           Reason           { get; set; }

    /// <summary>Üretilen SalesReturn tipi fatura.</summary>
    public Guid? ReturnInvoiceId { get; set; }

    public List<SalesReturnLine> Lines { get; set; } = new();

    public uint Version  { get; set; }
    public Guid TenantId { get; set; }
}
