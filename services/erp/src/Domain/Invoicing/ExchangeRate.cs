namespace ERP.Domain.Invoicing;

public class ExchangeRate : BaseEntity {
    public string   Currency { get; set; } = string.Empty;
    public DateOnly Date     { get; set; }
    public RateType Type     { get; set; }
    public decimal  Rate     { get; set; }

    /// <summary>"tcmb" | "manual"</summary>
    public string Source { get; set; } = "tcmb";
}
