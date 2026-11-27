namespace ERP.Domain.Shipping;

public sealed record ParcelContent(Guid ShipmentLineId, decimal Quantity);

/// <summary>Koli. Desi, ölçülerden Desi.Of ile hesaplanıp saklanır.</summary>
public class Parcel : BaseEntity, ITenantOwned {
    public Guid     ShipmentId { get; set; }
    public int      Index      { get; set; }
    public decimal  WeightKg   { get; set; }
    public decimal  WidthCm    { get; set; }
    public decimal  LengthCm   { get; set; }
    public decimal  HeightCm   { get; set; }
    public decimal  Desi       { get; set; }
    public IReadOnlyList<ParcelContent> Contents { get; set; } = [];
    public Guid     TenantId   { get; set; }
}
