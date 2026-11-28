namespace ERP.Domain.Pos;

public class PosPayment : BaseEntity, ITenantOwned {
    public Guid             SaleId   { get; set; }
    public PosPaymentMethod Method   { get; set; }

    /// <summary>Müşterinin verdiği tutar. Nakitte kasaya net giren tutar için ChangeCalculator kullanılır.</summary>
    public decimal Amount   { get; set; }
    public Guid    TenantId { get; set; }
}
