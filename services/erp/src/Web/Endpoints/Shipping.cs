using ERP.Application.Sales.Shipments;
using ERP.Application.Shipping.Delivery;
using ERP.Application.Shipping.Picking;
using ERP.Application.Shipping.Plans;
using ERP.Domain.Shipping;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Shipping : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/shipping";

    public sealed record CompleteBody(bool Force);

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(ShippingPickLists, "pick-lists");
        groupBuilder.MapPost(ShippingBatchPick, "pick-lists/batch");
        groupBuilder.MapGet(ShippingGetPickList, "pick-lists/{id:guid}");
        groupBuilder.MapPut(ShippingUpdatePickLine, "pick-lists/{id:guid}/lines/{lineId:guid}");
        groupBuilder.MapPost(ShippingCompletePickList, "pick-lists/{id:guid}/complete");
        groupBuilder.MapPost(ShippingCancelPickList, "pick-lists/{id:guid}/cancel");
        groupBuilder.MapGet(ShippingPickListPdf, "pick-lists/{id:guid}/pdf");

        groupBuilder.MapGet(ShippingPlans, "plans");
        groupBuilder.MapPost(ShippingCreatePlan, "plans");
        groupBuilder.MapGet(ShippingGetPlan, "plans/{id:guid}");
        groupBuilder.MapPut(ShippingReorderStops, "plans/{id:guid}/stops");
        groupBuilder.MapPost(ShippingDeliverStop, "plans/{id:guid}/stops/{stopId:guid}/deliver");
        groupBuilder.MapPost(ShippingFailStop, "plans/{id:guid}/stops/{stopId:guid}/fail");
        groupBuilder.MapGet(ShippingPlanPdf, "plans/{id:guid}/pdf");
    }

    [EndpointSummary("Toplama listeleri")]
    [EndpointDescription("sevkiyat.read. Süzgeç: status (open | completed | cancelled).")]
    public static async Task<Ok<IReadOnlyList<PickListRowDto>>> ShippingPickLists(ISender sender, PickListStatus? status) {
        return TypedResults.Ok(await sender.Send(new ListPickListsQuery(status)));
    }

    [EndpointSummary("Toplu toplama listesi")]
    [EndpointDescription("sevkiyat.pick. Gövde: { shipmentIds[] }. Aynı depodaki taslak sevkleri tek listede birleştirir (satırlar kalem bazında gruplanır, her satır sevk satırlarına bağlıdır). Sevkler picking olur.")]
    public static async Task<Created<PickListDto>> ShippingBatchPick(ISender sender, BatchPickInput input) {
        var list = await sender.Send(new BatchPickListCommand(input));
        return TypedResults.Created($"/api/erp/shipping/pick-lists/{list.Id}", list);
    }

    [EndpointSummary("Toplama listesi")]
    [EndpointDescription("sevkiyat.read.")]
    public static async Task<Ok<PickListDto>> ShippingGetPickList(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetPickListQuery(id)));
    }

    [EndpointSummary("Toplama satırı")]
    [EndpointDescription("sevkiyat.pick. Gövde: { pickedQty, lots?, serials? }. Eksik toplama (kısa toplama) serbesttir; lot verilmezse FEFO önerisinden türetilir. 400 picked_exceeds_quantity | lot_quantity_mismatch | serial_quantity_mismatch, 409 pick_list_closed.")]
    public static async Task<Ok<PickListDto>> ShippingUpdatePickLine(ISender sender, Guid id, Guid lineId, PickLineInput input) {
        return TypedResults.Ok(await sender.Send(new UpdatePickLineCommand(id, lineId, input)));
    }

    [EndpointSummary("Toplamayı tamamla")]
    [EndpointDescription("sevkiyat.pick. Gövde: { force? }. İşlenmemiş satır varsa ve force yoksa 409 pick_incomplete. Kısa toplama sevk satırı miktarını düşürür, fark siparişin açık miktarında kalır; hiç kalemi kalmayan sevk için 409 pick_empty.")]
    public static async Task<Ok<PickListDto>> ShippingCompletePickList(ISender sender, Guid id, CompleteBody? body) {
        return TypedResults.Ok(await sender.Send(new CompletePickListCommand(id, body?.Force ?? false)));
    }

    [EndpointSummary("Toplama listesini iptal et")]
    [EndpointDescription("sevkiyat.pick. Yalnızca açık liste; sevkler taslağa döner.")]
    public static async Task<Ok<PickListDto>> ShippingCancelPickList(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new CancelPickListCommand(id)));
    }

    [EndpointSummary("Toplama listesi PDF")]
    [EndpointDescription("sevkiyat.read.")]
    public static async Task<FileContentHttpResult> ShippingPickListPdf(ISender sender, Guid id) {
        var file = await sender.Send(new GetPickListPdfQuery(id));
        return TypedResults.File(file.Content, "application/pdf", file.FileName);
    }

    [EndpointSummary("Teslimat planları")]
    [EndpointDescription("sevkiyat.read, modül teslimat-plani. Süzgeç: date, status.")]
    public static async Task<Ok<IReadOnlyList<PlanRowDto>>> ShippingPlans(ISender sender, DateOnly? date, DeliveryPlanStatus? status) {
        return TypedResults.Ok(await sender.Send(new ListPlansQuery(date, status)));
    }

    [EndpointSummary("Teslimat planı oluştur")]
    [EndpointDescription("sevkiyat.plan, modül teslimat-plani. Gövde: { date, vehicle?, driverUserId? (üye), driverName? (serbest metin), shipmentIds[] (sıra = durak sırası) }. Duraklar \"Kendi aracımız\" ile sevk edilmiş (shipped) sevkler olmalıdır: 409 plan_stop_not_shipped; başka planda bekleyen durak 409 plan_stop_duplicate.")]
    public static async Task<Created<PlanDto>> ShippingCreatePlan(ISender sender, PlanInput input) {
        var plan = await sender.Send(new CreatePlanCommand(input));
        return TypedResults.Created($"/api/erp/shipping/plans/{plan.Id}", plan);
    }

    [EndpointSummary("Teslimat planı")]
    [EndpointDescription("sevkiyat.read, modül teslimat-plani. Duraklar sırayla; adres sevk anındaki kopyadır.")]
    public static async Task<Ok<PlanDto>> ShippingGetPlan(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetPlanQuery(id)));
    }

    [EndpointSummary("Durak sırası")]
    [EndpointDescription("sevkiyat.plan, modül teslimat-plani. Gövde: { stopIds[] } planın tüm duraklarını yeni sırayla bir kez içermelidir (400 stops_mismatch).")]
    public static async Task<Ok<PlanDto>> ShippingReorderStops(ISender sender, Guid id, StopOrderInput input) {
        return TypedResults.Ok(await sender.Send(new ReorderStopsCommand(id, input)));
    }

    [EndpointSummary("Durak teslim edildi")]
    [EndpointDescription("sevkiyat.deliver, modül teslimat-plani. Gövde sevk teslimiyle aynıdır: { receivedBy, deliveredAt?, proofFileId?, note? }. Sevk delivered olur.")]
    public static async Task<Ok<PlanDto>> ShippingDeliverStop(ISender sender, Guid id, Guid stopId, DeliverInput input) {
        return TypedResults.Ok(await sender.Send(new DeliverStopCommand(id, stopId, input)));
    }

    [EndpointSummary("Durak başarısız")]
    [EndpointDescription("sevkiyat.deliver, modül teslimat-plani. Gövde: { reason, at? }. Sevk deliveryFailed olur.")]
    public static async Task<Ok<PlanDto>> ShippingFailStop(ISender sender, Guid id, Guid stopId, FailInput input) {
        return TypedResults.Ok(await sender.Send(new FailStopCommand(id, stopId, input)));
    }

    [EndpointSummary("Rota föyü PDF")]
    [EndpointDescription("sevkiyat.read, modül teslimat-plani. Duraklar sırayla: adres, telefon, kalemler, imza satırı.")]
    public static async Task<FileContentHttpResult> ShippingPlanPdf(ISender sender, Guid id) {
        var file = await sender.Send(new GetPlanPdfQuery(id));
        return TypedResults.File(file.Content, "application/pdf", file.FileName);
    }
}
