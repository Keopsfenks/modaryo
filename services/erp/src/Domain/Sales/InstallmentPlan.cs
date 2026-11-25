namespace ERP.Domain.Sales;

/// <summary>Sipariş düzeyinde taksit planı. Satırlar peşinatı içermez.</summary>
public class InstallmentPlan : BaseEntity, ITenantOwned {
    public Guid     SalesOrderId         { get; set; }
    public decimal  DownPayment          { get; set; }
    public decimal  DownPaymentRemaining { get; set; }
    public int      InstallmentCount     { get; set; }
    public DateOnly FirstDueDate         { get; set; }

    public List<InstallmentPlanRow> Rows { get; set; } = new();
    public Guid TenantId { get; set; }
}

public class InstallmentPlanRow : BaseEntity, ITenantOwned {
    public Guid     InstallmentPlanId { get; set; }
    public int      Sequence          { get; set; }
    public DateOnly DueDate           { get; set; }

    /// <summary>Planlanan tutar.</summary>
    public decimal Amount { get; set; }

    /// <summary>Faturalarca henüz tüketilmemiş tutar.</summary>
    public decimal Remaining { get; set; }
    public Guid    TenantId  { get; set; }
}
