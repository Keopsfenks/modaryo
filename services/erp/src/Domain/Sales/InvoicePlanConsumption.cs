namespace ERP.Domain.Sales;

/// <summary>Kesilmiş bir faturanın taksit planından tükettiği tutar (iptalde geri verilir). PlanRowId null ise peşinattır.</summary>
public class InvoicePlanConsumption : BaseEntity, ITenantOwned {
    public Guid    InvoiceId    { get; set; }
    public Guid    SalesOrderId { get; set; }
    public Guid?   PlanRowId    { get; set; }
    public decimal Amount       { get; set; }
    public Guid    TenantId     { get; set; }
}
