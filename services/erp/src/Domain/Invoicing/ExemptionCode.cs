namespace ERP.Domain.Invoicing;

/// <summary>KDV istisna / muafiyet kodu (GİB UBL-TR).</summary>
public class ExemptionCode : BaseEntity {
    public string Code     { get; set; } = string.Empty;
    public string Name     { get; set; } = string.Empty;

    /// <summary>"istisna" | "ihracat" | "ozel-matrah" vb.</summary>
    public string Group    { get; set; } = "istisna";

    public bool   IsActive { get; set; } = true;
}
