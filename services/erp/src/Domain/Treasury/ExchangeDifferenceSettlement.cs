namespace ERP.Domain.Treasury;

/// <summary>İşlenmiş kur farkı: kapsadığı tahsisat kimlikleri, tutar ve oluşan belge. Her fark bir kez işlenir.</summary>
public class ExchangeDifferenceSettlement : BaseAuditableEntity, ITenantOwned {
    public Guid       CurrentAccountId { get; set; }
    public decimal    Amount           { get; set; }

    /// <summary>Invoice veya Expense.</summary>
    public string     ResultType       { get; set; } = string.Empty;
    public Guid?      ResultId         { get; set; }
    public List<Guid> AllocationIds    { get; set; } = new();
    public Guid       TenantId         { get; set; }
}
