using ERP.Domain.Invoicing;

namespace ERP.Domain.Shipping;

public enum DeliveryPlanStatus { Planned, InProgress, Completed, Cancelled }

/// <summary>Teslimat planı (kendi araç/rota). Rota optimizasyonu kapsam dışıdır.</summary>
public class DeliveryPlan : BaseAuditableEntity, ITenantOwned {
    public string             Number     { get; set; } = string.Empty;
    public Guid               BranchId   { get; set; }
    public DateOnly           Date       { get; set; }
    public string?            Vehicle    { get; set; }

    /// <summary>Sürücü üye ise kullanıcı kimliği, değilse serbest metin.</summary>
    public Guid?              DriverUserId { get; set; }
    public string?            DriverName   { get; set; }
    public DeliveryPlanStatus Status     { get; set; } = DeliveryPlanStatus.Planned;
    public List<DeliveryStop> Stops      { get; set; } = new();
    public uint               Version    { get; set; }
    public Guid               TenantId   { get; set; }
}

public enum StopStatus { Pending, Delivered, Failed }

public class DeliveryStop : BaseEntity, ITenantOwned {
    public Guid             DeliveryPlanId { get; set; }
    public int              Sort           { get; set; }
    public Guid             ShipmentId     { get; set; }
    public AddressSnapshot? Address        { get; set; }
    public StopStatus       Status         { get; set; } = StopStatus.Pending;
    public string?          Note           { get; set; }
    public Guid             TenantId       { get; set; }
}
