using ERP.Application.Purchasing.Orders;
using ERP.Domain.Purchasing;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Purchasing : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/purchasing";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(ListOrders, "orders");
        groupBuilder.MapPost(CreateOrder, "orders");
        groupBuilder.MapGet(GetOrder, "orders/{id:guid}");
        groupBuilder.MapPut(UpdateOrder, "orders/{id:guid}");
        groupBuilder.MapPost(ApproveOrder, "orders/{id:guid}/approve");
        groupBuilder.MapPost(CancelOrder, "orders/{id:guid}/cancel");
        groupBuilder.MapPost(CloseOrder, "orders/{id:guid}/close");
        groupBuilder.MapPost(CopyOrder, "orders/{id:guid}/copy");
        groupBuilder.MapGet(OrderPdf, "orders/{id:guid}/pdf");
        groupBuilder.MapPost(OrderEmail, "orders/{id:guid}/email");
    }

    [EndpointSummary("Satın alma siparişleri")]
    [EndpointDescription("satinalma.read. Süzgeçler: status, supplierId, overdue (onaylı/kısmen gelmiş ve açık satırın beklenen tarihi geçmiş), search (sipariş no / tedarikçi adı), page, pageSize. fiyat.alis yoksa tutarlar null döner.")]
    public static async Task<Ok<PoListResult>> ListOrders(ISender sender, PurchaseOrderStatus? status, Guid? supplierId, bool? overdue, string? search,
                                                          int page = 1, int pageSize = 25) {
        return TypedResults.Ok(await sender.Send(new ListPosQuery(status, supplierId, overdue, search, page, pageSize)));
    }

    [EndpointSummary("Sipariş taslağı oluştur")]
    [EndpointDescription("satinalma.manage + fiyat.alis. Gövde: supplierId (tedarikçi cari), branchId, date, expectedDate?, currency, rate?, notes?, lines[{ productId, variantId?, unit?, quantity, width?, length?, unitPrice?, discountPercent?, vatRate?, expectedDate?, salesOrderLineId? }]. unitPrice verilmezse alış fiyatı önceliğiyle (tedarikçi listesi, varsayılan liste, kart) belirlenir. Yanıt satırlarında lastPurchase { unitPriceTl, date } (kesilmiş alış faturalarından son alış) bulunur. 201 + sipariş. 400 cari_not_supplier.")]
    public static async Task<Created<PoDto>> CreateOrder(ISender sender, PoInput input) {
        var po = await sender.Send(new CreatePoCommand(input));
        return TypedResults.Created($"/api/erp/purchasing/orders/{po.Id}", po);
    }

    [EndpointSummary("Sipariş detayı")]
    [EndpointDescription("satinalma.read. Satırlar (son alış ipucu), üçlü karşılaştırma satırları (matchRows), mal kabuller ve faturalar. fiyat.alis yoksa tüm fiyat ve tutar alanları null döner.")]
    public static async Task<Ok<PoDto>> GetOrder(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetPoQuery(id)));
    }

    [EndpointSummary("Taslak siparişi güncelle")]
    [EndpointDescription("satinalma.manage + fiyat.alis. Yalnızca taslak: aksi halde 409 po_not_draft. version verilirse eşleşmeyen 409 version_conflict.")]
    public static async Task<Ok<PoDto>> UpdateOrder(ISender sender, Guid id, PoInput input) {
        return TypedResults.Ok(await sender.Send(new UpdatePoCommand(id, input)));
    }

    [EndpointSummary("Siparişi onayla")]
    [EndpointDescription("satinalma.manage. Taslak -> Onaylı. 409 po_not_draft.")]
    public static async Task<Ok<PoDto>> ApproveOrder(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new ApprovePoCommand(id)));
    }

    public sealed record ReasonBody(string Reason);

    [EndpointSummary("Siparişi iptal et")]
    [EndpointDescription("satinalma.manage. Gövde: { reason }. Yalnızca mal kabulü olmayan sipariş iptal edilir; aksi halde 409 po_has_receipts (kısa kapatma kullanın).")]
    public static async Task<Ok<PoDto>> CancelOrder(ISender sender, Guid id, ReasonBody body) {
        return TypedResults.Ok(await sender.Send(new CancelPoCommand(id, body.Reason)));
    }

    [EndpointSummary("Siparişi kısa kapat")]
    [EndpointDescription("satinalma.manage. Gövde: { reason }. Onaylı ya da kısmen gelmiş siparişin kalan açık miktarını serbest bırakır; sonraki mal kabul 409 receipt_exceeds_open ile reddedilir.")]
    public static async Task<Ok<PoDto>> CloseOrder(ISender sender, Guid id, ReasonBody body) {
        return TypedResults.Ok(await sender.Send(new ClosePoCommand(id, body.Reason)));
    }

    [EndpointSummary("Siparişi kopyala")]
    [EndpointDescription("satinalma.manage + fiyat.alis. Aynı tedarikçi ve satırlarla yeni bir taslak üretir. 201 + yeni sipariş.")]
    public static async Task<Created<PoDto>> CopyOrder(ISender sender, Guid id) {
        var po = await sender.Send(new CopyPoCommand(id));
        return TypedResults.Created($"/api/erp/purchasing/orders/{po.Id}", po);
    }

    [EndpointSummary("Sipariş PDF'i")]
    [EndpointDescription("satinalma.read. A4 PDF: tedarikçi, satırlar ve beklenen tarihler. fiyat.alis yoksa fiyat sütunları ve toplamlar basılmaz.")]
    public static async Task<FileContentHttpResult> OrderPdf(ISender sender, Guid id) {
        var file = await sender.Send(new GetPoPdfQuery(id));
        return TypedResults.File(file.Content, "application/pdf", file.FileName);
    }

    public sealed record EmailBody(List<string>? To, List<string>? Cc, string? Subject, string? Body);

    [EndpointSummary("Siparişi tedarikçiye e-postayla gönder")]
    [EndpointDescription("satinalma.manage. Gövde: { to[]?, cc[]?, subject?, body? }. to verilmezse tedarikçi kartındaki e-posta kullanılır (400 no_recipient). Sipariş PDF olarak eklenir. 409: po_not_approved (taslak), po_cancelled; 502 mail_send_failed.")]
    public static async Task<NoContent> OrderEmail(ISender sender, Guid id, EmailBody body) {
        await sender.Send(new SendPoEmailCommand(id, body.To, body.Cc, body.Subject, body.Body));
        return TypedResults.NoContent();
    }
}
