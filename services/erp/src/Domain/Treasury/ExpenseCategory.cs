namespace ERP.Domain.Treasury;

/// <summary>Masraf veya gelir kategorisi; muhasebe aktarımında kendi hesap koduna eşlenir.</summary>
public class ExpenseCategory : BaseAuditableEntity, ITenantOwned {
    public string Code        { get; set; } = string.Empty;
    public string Name        { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public bool   IsIncome    { get; set; }
    public bool   IsSystem    { get; set; }
    public bool   IsActive    { get; set; } = true;
    public Guid   TenantId    { get; set; }
}
