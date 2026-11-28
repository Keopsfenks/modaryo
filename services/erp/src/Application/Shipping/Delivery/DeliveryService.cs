using ERP.Application.Common.Interfaces;
using ERP.Application.Sales.Shipments;
using ERP.Application.Settings.Categories;
using ERP.Application.Shipping.Dispatch;
using ERP.Domain.Sales;
using ERP.Domain.Shipping;

namespace ERP.Application.Shipping.Delivery;

public sealed record FailInput(string Reason, DateTimeOffset? At);

public sealed record ResendInput(string? CarrierCode, string? TrackingNo);

/// <summary>
///     Teslimat yaşam döngüsü: teslim (kanıtlı), başarısız teslimat ve yeniden gönderim. Çağıran açık bir işlem içinde, sevkin siparişinin kilidi alınmış olarak
///     ve sevki Lines + Attempts ile izlenir yüklemiş olarak çağırır; bu sınıf işlem açmaz ya da onaylamaz (SaveChanges çağırır). Yeniden gönderim stoğa dokunmaz.
/// </summary>
public sealed class DeliveryService(
    IApplicationDbContext db,
    IOutbox               outbox,
    ShipmentService       shipments,
    TimeProvider          clock
) {
    private const string ImageKind = "image";

    public Task<Shipment> LoadAsync(Guid id, CancellationToken ct) {
        return db.Shipments.Include(s => s.Lines).Include(s => s.Attempts).FirstAsync(s => s.Id == id, ct);
    }

    /// <summary>Sevkin açık (sonuçlanmamış) son denemesi; eski (P7) sevklerde deneme satırı yoksa ilk deneme oluşturulur.</summary>
    private DeliveryAttempt CurrentAttempt(Shipment shipment) {
        var attempt = shipment.Attempts.OrderByDescending(a => a.AttemptNo).FirstOrDefault();
        if (attempt is not null && attempt.Outcome == DeliveryOutcome.Pending) return attempt;
        if (attempt is not null) throw new InvalidOperationException("Sevkin açık teslimat denemesi yok.");

        attempt = new DeliveryAttempt { ShipmentId = shipment.Id, AttemptNo = 1, SentAt = shipment.DispatchAt ?? shipment.ShippedAt ?? clock.GetUtcNow() };
        shipment.Attempts.Add(attempt);
        return attempt;
    }

    public async Task DeliverAsync(Shipment shipment, DeliverInput input, CancellationToken ct) {
        if (shipment.Status != ShipmentStatus.Shipped) throw ShippingErrors.InvalidTransition(shipment.Status.ToString(), "Delivered");
        if (input.ProofFileId is { } fileId && !await db.StoredFiles.AsNoTracking().AnyAsync(f => f.Id == fileId && f.Kind == ImageKind, ct))
            throw ShippingErrors.Bad("proof_file_invalid", "Teslim kanıtı, firmaya ait \"image\" türünde bir dosya olmalıdır.");

        var at       = (input.DeliveredAt ?? clock.GetUtcNow()).ToUniversalTime();
        var receiver = input.ReceivedBy!.Trim();
        var attempt  = CurrentAttempt(shipment);
        attempt.Outcome    = DeliveryOutcome.Delivered;
        attempt.ResolvedAt = at;

        shipment.Status      = ShipmentStatus.Delivered;
        shipment.DeliveredAt = at;
        shipment.ReceivedBy  = receiver;
        db.DeliveryProofs.Add(new DeliveryProof {
            ShipmentId = shipment.Id, ReceivedBy = receiver, DeliveredAt = at, PhotoFileId = input.ProofFileId, Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim()
        });
        outbox.Enqueue("sales.shipment-delivered", new { shipmentId = shipment.Id, number = shipment.Number, orderId = shipment.SalesOrderId });
        await db.SaveChangesAsync(ct);
    }

    public async Task FailAsync(Shipment shipment, FailInput input, CancellationToken ct) {
        if (shipment.Status != ShipmentStatus.Shipped) throw ShippingErrors.InvalidTransition(shipment.Status.ToString(), "DeliveryFailed");

        var attempt = CurrentAttempt(shipment);
        attempt.Outcome       = DeliveryOutcome.Failed;
        attempt.ResolvedAt    = (input.At ?? clock.GetUtcNow()).ToUniversalTime();
        attempt.FailureReason = input.Reason.Trim();

        shipment.Status = ShipmentStatus.DeliveryFailed;
        outbox.Enqueue("shipment.delivery-failed", new { shipmentId = shipment.Id, number = shipment.Number, orderId = shipment.SalesOrderId, attemptNo = attempt.AttemptNo, reason = attempt.FailureReason });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Başarısız teslimattan sonra yeniden gönderim: yeni deneme açılır, durum Shipped olur. Stok çıkışı ilk sevkte yapılmıştır; burada yazılmaz.</summary>
    public async Task ResendAsync(Shipment shipment, ShippingSettings settings, ResendInput input, CancellationToken ct) {
        if (shipment.Status != ShipmentStatus.DeliveryFailed) throw ShippingErrors.InvalidTransition(shipment.Status.ToString(), "Shipped");

        var carrierCode = string.IsNullOrWhiteSpace(input.CarrierCode) ? shipment.CarrierCode : input.CarrierCode.Trim();
        var changed     = carrierCode != shipment.CarrierCode;
        var tracking    = !string.IsNullOrWhiteSpace(input.TrackingNo) ? input.TrackingNo.Trim() : changed ? null : shipment.TrackingNo;
        var carrier     = CarrierRules.Resolve(settings, carrierCode);
        CarrierRules.EnsureTracking(carrier, tracking);

        var now = clock.GetUtcNow();
        var attemptNo = shipment.Attempts.Count == 0 ? 2 : shipment.Attempts.Max(a => a.AttemptNo) + 1;
        shipment.Attempts.Add(new DeliveryAttempt { ShipmentId = shipment.Id, AttemptNo = attemptNo, SentAt = now });
        shipment.Status      = ShipmentStatus.Shipped;
        shipment.CarrierCode = carrier.Code;
        shipment.TrackingNo  = tracking;
        shipments.EnqueueShipped(shipment, attemptNo, true);
        await db.SaveChangesAsync(ct);
    }
}
