using ERP.Domain.Invoicing;
using ERP.Domain.Shipping;

namespace ERP.Domain.Sales;

public class Shipment : BaseAuditableEntity, ITenantOwned {
    public string          Number           { get; set; } = string.Empty;
    public ShipmentStatus  Status           { get; set; } = ShipmentStatus.Draft;
    public Guid            SalesOrderId     { get; set; }
    public Guid            CurrentAccountId { get; set; }
    public Guid            BranchId         { get; set; }
    public Guid            WarehouseId      { get; set; }
    public DateOnly        Date             { get; set; }
    public DateTimeOffset? ShippedAt        { get; set; }
    public DateTimeOffset? DeliveredAt      { get; set; }

    /// <summary>Teslimat adresi (sevk anında dondurulur).</summary>
    public AddressSnapshot? DeliveryAddress { get; set; }
    public string?          CargoNote       { get; set; }
    public string?          ReceivedBy      { get; set; }
    public string?          Notes           { get; set; }
    public string?          CancelReason    { get; set; }

    /// <summary>Faturalandıysa üretilen satış faturası.</summary>
    public Guid? InvoiceId { get; set; }

    /// <summary>Kargo firması kodu (ShippingSettings.Carriers) ve takip numarası.</summary>
    public string? CarrierCode { get; set; }
    public string? TrackingNo  { get; set; }

    /// <summary>İrsaliye numarası (IR{yyyy}{000001}, şube bazında) ve fiili çıkış zamanı.</summary>
    public string?          IrsaliyeNo { get; set; }
    public DateTimeOffset?  DispatchAt { get; set; }
    public string?          Vehicle    { get; set; }
    public string?          Driver     { get; set; }

    /// <summary>Toplama listesi (Picking durumunda dolu).</summary>
    public Guid?            PickListId { get; set; }

    public List<ShipmentLine>    Lines    { get; set; } = new();
    public List<Parcel>          Parcels  { get; set; } = new();
    public List<DeliveryAttempt> Attempts { get; set; } = new();

    public uint Version  { get; set; }
    public Guid TenantId { get; set; }
}
