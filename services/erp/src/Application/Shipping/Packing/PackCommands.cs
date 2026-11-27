using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Sales.Shipments;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;
using ERP.Domain.Sales;
using ERP.Domain.Shipping;

namespace ERP.Application.Shipping.Packing;

public sealed record ParcelContentInput(Guid ShipmentLineId, decimal Qty);

public sealed record ParcelInput(decimal WeightKg, decimal WidthCm, decimal LengthCm, decimal HeightCm, IReadOnlyList<ParcelContentInput> Contents);

public sealed record PackInput(IReadOnlyList<ParcelInput> Parcels);

/// <summary>Koli listesini yazar (mevcut koliler değiştirilir) ve sevki Packed durumuna geçirir. Toplama listesi tamamlanmış olmalıdır.</summary>
[Authorize(Permission = ShippingPermissions.Pack)]
public sealed record PackShipmentCommand(Guid ShipmentId, PackInput Input) : IRequest<ShipmentDto>;

public sealed class PackShipmentCommandValidator : AbstractValidator<PackShipmentCommand> {
    public PackShipmentCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.Parcels).Must(p => p is { Count: <= 100 }).WithMessage("Bir sevke en fazla 100 koli girilebilir.");
        RuleForEach(c => c.Input.Parcels).ChildRules(p => {
            p.RuleFor(x => x.WeightKg).GreaterThan(0m).LessThanOrEqualTo(10000m).WithMessage("Koli ağırlığı 0 ile 10000 kg arasında olmalıdır.");
            p.RuleFor(x => x.WidthCm).GreaterThan(0m).LessThanOrEqualTo(1000m).WithMessage("Koli eni 0 ile 1000 cm arasında olmalıdır.");
            p.RuleFor(x => x.LengthCm).GreaterThan(0m).LessThanOrEqualTo(1000m).WithMessage("Koli boyu 0 ile 1000 cm arasında olmalıdır.");
            p.RuleFor(x => x.HeightCm).GreaterThan(0m).LessThanOrEqualTo(1000m).WithMessage("Koli yüksekliği 0 ile 1000 cm arasında olmalıdır.");
            p.RuleFor(x => x.Contents).NotEmpty().WithMessage("Her kolinin içeriği girilmelidir.");
            p.RuleForEach(x => x.Contents).ChildRules(c => c.RuleFor(y => y.Qty).GreaterThan(0m).WithMessage("Koli içerik miktarı sıfırdan büyük olmalıdır."));
        });
    }
}

public sealed class PackShipmentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ITenantSettings       settings,
    ShipmentMapper        mapper
) : IRequestHandler<PackShipmentCommand, ShipmentDto> {
    public async Task<ShipmentDto> Handle(PackShipmentCommand request, CancellationToken ct) {
        var head = await db.Shipments.AsNoTracking().Where(s => s.Id == request.ShipmentId).Select(s => new { s.SalesOrderId, s.BranchId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.ShipmentId.ToString(), "Shipment");
        BranchGuard.Ensure(tenant, head.BranchId);

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            if (!await db.LockSalesOrderAsync(head.SalesOrderId, ct)) throw new NotFoundException(head.SalesOrderId.ToString(), "SalesOrder");
            db.ClearTracking();
            var shipment = await db.Shipments.Include(s => s.Lines).Include(s => s.Parcels).FirstAsync(s => s.Id == request.ShipmentId, ct);
            var shipping = await settings.GetAsync<ShippingSettings>(ct);

            // Packed durumundaki sevkin kolileri yeniden yazılabilir (durum değişmez); diğer durumlar makineye tabidir.
            var current = shipment.Status.ToString();
            if (shipment.Status != ShipmentStatus.Packed && !ShipmentStatusMachine.CanMove(current, "Packed", shipping.UsePickPack))
                throw ShippingErrors.InvalidTransition(current, "Packed");

            var parcels = request.Input.Parcels;
            if (parcels.Count == 0) throw ShippingErrors.ParcelRequired();

            if (shipment.PickListId is { } pickListId) {
                var status = await db.PickLists.AsNoTracking().Where(p => p.Id == pickListId).Select(p => p.Status).FirstAsync(ct);
                if (status != PickListStatus.Completed) throw ShippingErrors.PickIncomplete();
            } else {
                throw ShippingErrors.PickIncomplete();
            }

            var productIds = shipment.Lines.Select(l => l.ProductId).Distinct().ToList();
            var services   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id) && p.Kind == ProductKind.Service).Select(p => p.Id).ToListAsync(ct);
            var physical   = shipment.Lines.Where(l => !services.Contains(l.ProductId)).ToDictionary(l => l.Id);

            var packed = new Dictionary<Guid, decimal>();
            foreach (var parcel in parcels)
                foreach (var content in parcel.Contents) {
                    if (!physical.ContainsKey(content.ShipmentLineId))
                        throw ShippingErrors.Bad("parcel_line_invalid", "Koli içeriği sevkin fiziksel bir satırına ait olmalıdır.");
                    if (Math.Round(content.Qty, 6) != content.Qty) throw ShippingErrors.Bad("quantity_scale", "Koli içerik miktarı en fazla 6 ondalık basamak içerebilir.");
                    packed[content.ShipmentLineId] = packed.GetValueOrDefault(content.ShipmentLineId) + content.Qty;
                }

            foreach (var line in physical.Values) {
                var qty = packed.GetValueOrDefault(line.Id);
                if (qty != line.Quantity)
                    throw ShippingErrors.Bad("parcel_contents_mismatch", $"\"{line.Description}\" için koli içerikleri toplamı {line.Quantity:0.######} olmalıdır (girilen {qty:0.######}).");
            }

            if (shipment.Parcels.Count > 0) {
                // Aynı (sevk, sıra) dizinine yeniden yazılacağından eski koliler önce silinir.
                db.Parcels.RemoveRange(shipment.Parcels);
                shipment.Parcels.Clear();
                await db.SaveChangesAsync(ct);
            }

            var index = 1;
            foreach (var p in parcels)
                shipment.Parcels.Add(new Parcel {
                    ShipmentId = shipment.Id, Index = index++, WeightKg = p.WeightKg, WidthCm = p.WidthCm, LengthCm = p.LengthCm, HeightCm = p.HeightCm,
                    Desi = Desi.Of(p.WidthCm, p.LengthCm, p.HeightCm), Contents = p.Contents.Select(c => new ParcelContent(c.ShipmentLineId, c.Qty)).ToList()
                });

            shipment.Status = ShipmentStatus.Packed;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(shipment, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}
