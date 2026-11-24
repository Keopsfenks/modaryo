namespace ERP.Domain.CurrentAccounts;

public class CurrentAccount : BaseAuditableEntity, ITenantOwned {
    public string      Code               { get; set; } = string.Empty;
    public string      Name               { get; set; } = string.Empty;
    public AccountKind Kind               { get; set; }
    public string?     TaxNumber          { get; set; }
    public string?     TaxOffice          { get; set; }
    public bool        IsEInvoiceTaxpayer { get; set; }
    public bool        IsCustomer         { get; set; }
    public bool        IsSupplier         { get; set; }
    public bool        IsEmployee         { get; set; }
    public string?     Phone              { get; set; }
    public string?     Email              { get; set; }
    public string?     Notes              { get; set; }
    public string      Currency           { get; set; } = "TRY";
    public int         DefaultDueDays     { get; set; } = 30;
    public decimal?    CreditLimit        { get; set; }
    public bool        IsActive           { get; set; } = true;

    /// <summary>Müşteri grubu (fiyat listesi önceliği için); aktif firmanın grubu olmalı.</summary>
    public Guid?       CustomerGroupId    { get; set; }

    public ICollection<CurrentAccountAddress> Addresses { get; set; } = new List<CurrentAccountAddress>();
    public Guid                               TenantId  { get; set; }
}