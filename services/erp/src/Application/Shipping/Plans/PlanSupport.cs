using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Invoicing;
using ERP.Domain.Shipping;

namespace ERP.Application.Shipping.Plans;

public static class PlanRules {
    /// <summary>Teslimat planı modülünün anahtarı (kimlik servisi ModuleKeys ile aynı).</summary>
    public const string Module = "teslimat-plani";

    public static AppException StopNotShipped() {
        return ShippingErrors.Conflict("plan_stop_not_shipped", "Plana yalnızca \"Kendi aracımız\" ile sevk edilmiş (Shipped) sevkler eklenebilir.");
    }

    /// <summary>Tüm duraklar sonuçlandıysa plan tamamlanır; en az biri sonuçlandıysa sürüyordur.</summary>
    public static void Refresh(DeliveryPlan plan) {
        if (plan.Status == DeliveryPlanStatus.Cancelled) return;
        var resolved = plan.Stops.Count(s => s.Status != StopStatus.Pending);
        plan.Status = plan.Stops.Count > 0 && resolved == plan.Stops.Count ? DeliveryPlanStatus.Completed : resolved > 0 ? DeliveryPlanStatus.InProgress : DeliveryPlanStatus.Planned;
    }
}

public sealed record PlanStopDto(
    Guid             Id,
    int              Sort,
    Guid             ShipmentId,
    string           ShipmentNumber,
    string           CustomerName,
    string?          CustomerPhone,
    AddressSnapshot? Address,
    StopStatus       Status,
    string?          Note
);

public sealed record PlanDto(
    Guid                     Id,
    string                   Number,
    Guid                     BranchId,
    DateOnly                 Date,
    string?                  Vehicle,
    Guid?                    DriverUserId,
    string?                  DriverName,
    DeliveryPlanStatus       Status,
    uint                     Version,
    IReadOnlyList<PlanStopDto> Stops
);

public sealed record PlanRowDto(Guid Id, string Number, DateOnly Date, string? Vehicle, string? DriverName, DeliveryPlanStatus Status, int StopCount, int DoneCount);

public sealed record PlanInput(DateOnly Date, string? Vehicle, Guid? DriverUserId, string? DriverName, IReadOnlyList<Guid> ShipmentIds);

public sealed record StopOrderInput(IReadOnlyList<Guid> StopIds);

public sealed class PlanMapper(IApplicationDbContext db) {
    public async Task<PlanDto> ToDtoAsync(DeliveryPlan plan, CancellationToken ct) {
        var stops       = plan.Stops.OrderBy(s => s.Sort).ToList();
        var shipmentIds = stops.Select(s => s.ShipmentId).ToList();
        var shipments   = await db.Shipments.AsNoTracking().Where(s => shipmentIds.Contains(s.Id)).Select(s => new { s.Id, s.Number, s.CurrentAccountId }).ToDictionaryAsync(s => s.Id, ct);
        var cariIds     = shipments.Values.Select(s => s.CurrentAccountId).Distinct().ToList();
        var cariRows    = await db.CurrentAccounts.AsNoTracking().Where(c => cariIds.Contains(c.Id)).Select(c => new { c.Id, c.Name, c.Phone }).ToDictionaryAsync(c => c.Id, ct);
        var rows = stops.Select(s => {
            var shipment = shipments.GetValueOrDefault(s.ShipmentId);
            var cari     = shipment is null ? null : cariRows.GetValueOrDefault(shipment.CurrentAccountId);
            return new PlanStopDto(s.Id, s.Sort, s.ShipmentId, shipment?.Number ?? string.Empty, cari?.Name ?? s.Address?.Title ?? string.Empty, cari?.Phone, s.Address, s.Status, s.Note);
        }).ToList();
        return new PlanDto(plan.Id, plan.Number, plan.BranchId, plan.Date, plan.Vehicle, plan.DriverUserId, plan.DriverName, plan.Status, plan.Version, rows);
    }

    public static string? Clean(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record PlanPdfFile(byte[] Content, string FileName);
