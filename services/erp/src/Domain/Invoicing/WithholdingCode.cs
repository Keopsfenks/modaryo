namespace ERP.Domain.Invoicing;

/// <summary>Tevkifat kodu (GİB UBL-TR). Oran Tenths/10.</summary>
public class WithholdingCode : BaseEntity {
    public string Code     { get; set; } = string.Empty;
    public string Name     { get; set; } = string.Empty;
    public int    Tenths   { get; set; }
    public bool   IsActive { get; set; } = true;
}
