namespace ERP.Domain.Pos;

/// <summary>Vardiya içi kasa girişi/çıkışı (avans, para alma vb.).</summary>
public class CashMove : BaseEntity, ITenantOwned {
    public Guid              ShiftId         { get; set; }
    public CashMoveDirection Direction       { get; set; }
    public decimal           Amount          { get; set; }
    public string?           Note            { get; set; }
    public DateTimeOffset    At              { get; set; }
    public Guid?             CreatedByUserId { get; set; }
    public Guid              TenantId        { get; set; }
}
