namespace ERP.Domain.Pos;

/// <summary>Vardiya: kasiyer + kasa noktası. Kapanışta sayılan nakit ile beklenen nakit farkı saklanır.</summary>
public class Shift : BaseAuditableEntity, ITenantOwned {
    public Guid           RegisterId    { get; set; }
    public Guid           CashierUserId { get; set; }
    public ShiftStatus    Status        { get; set; } = ShiftStatus.Open;
    public DateTimeOffset OpenedAt      { get; set; }
    public decimal        OpeningCash   { get; set; }

    public DateTimeOffset? ClosedAt     { get; set; }
    public decimal?        CountedCash  { get; set; }
    public decimal?        ExpectedCash { get; set; }

    /// <summary>Sayılan - beklenen.</summary>
    public decimal? Difference { get; set; }

    public string? CloseNote { get; set; }

    public List<CashMove> CashMoves { get; set; } = new();

    public uint Version  { get; set; }
    public Guid TenantId { get; set; }
}
