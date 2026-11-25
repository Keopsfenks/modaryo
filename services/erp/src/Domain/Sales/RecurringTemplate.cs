namespace ERP.Domain.Sales;

public class RecurringTemplate : BaseAuditableEntity, ITenantOwned {
    public string             Number           { get; set; } = string.Empty;
    public string             Name             { get; set; } = string.Empty;
    public Guid               CurrentAccountId { get; set; }
    public Guid               BranchId         { get; set; }
    public string             Currency         { get; set; } = "TRY";
    public RecurrenceInterval Interval         { get; set; } = RecurrenceInterval.Monthly;
    public int                DayOfMonth       { get; set; } = 1;
    public DateOnly           StartDate        { get; set; }
    public DateOnly?          EndDate          { get; set; }
    public RecurringMode      Mode             { get; set; } = RecurringMode.Draft;
    public bool               IsActive         { get; set; } = true;
    public string?            Notes            { get; set; }

    public List<RecurringTemplateLine> Lines { get; set; } = new();
    public List<RecurringRun>          Runs  { get; set; } = new();

    public Guid TenantId { get; set; }
}

public class RecurringTemplateLine : BaseEntity, ITenantOwned {
    public Guid     RecurringTemplateId { get; set; }
    public int      Sort                { get; set; }
    public Guid?    ProductId           { get; set; }
    public Guid?    VariantId           { get; set; }
    public string   Description         { get; set; } = string.Empty;
    public string   Unit                { get; set; } = string.Empty;
    public decimal  Quantity            { get; set; }
    public decimal  UnitPrice           { get; set; }
    public bool     VatIncluded         { get; set; }
    public decimal? DiscountPercent     { get; set; }
    public int      VatRate             { get; set; }

    /// <summary>Sabit satırlar fiyat listesinden yeniden fiyatlanmaz.</summary>
    public bool IsFixedPrice { get; set; } = true;
    public Guid TenantId     { get; set; }
}

/// <summary>(şablon, dönem) başına tek üretim kaydı: iş tekrar çalışsa da ikinci fatura oluşmaz.</summary>
public class RecurringRun : BaseEntity, ITenantOwned {
    public Guid           RecurringTemplateId { get; set; }
    public DateOnly       Period              { get; set; }
    public Guid           InvoiceId           { get; set; }
    public DateTimeOffset GeneratedAt         { get; set; }
    public Guid           TenantId            { get; set; }
}
