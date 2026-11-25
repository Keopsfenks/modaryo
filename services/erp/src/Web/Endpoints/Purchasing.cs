using ERP.Application.Purchasing.Orders;
using ERP.Application.Purchasing.Quarantine;
using ERP.Application.Purchasing.Returns;
using ERP.Application.Purchasing.Receipts;
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
        groupBuilder.MapPost(CreateReceipt, "receipts");
        groupBuilder.MapGet(ListReceipts, "receipts");
        groupBuilder.MapGet(GetReceipt, "receipts/{id:guid}");
        groupBuilder.MapPost(CancelReceipt, "receipts/{id:guid}/cancel");
        groupBuilder.MapGet(ListQuarantine, "quarantine");
        groupBuilder.MapPost(ReleaseQuarantine, "quarantine/release");
        groupBuilder.MapPost(RejectQuarantine, "quarantine/reject");
        groupBuilder.MapPost(CreateReturn, "returns");
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

    [EndpointSummary("Mal kabul")]
    [EndpointDescription("satinalma.receive. Gövde: purchaseOrderId?, supplierId? (siparişsizde zorunlu), branchId, warehouseId, date, deliveryNoteNo?, notes?, currency? (siparişsizde), lines[{ poLineId?, productId, variantId?, unit?, quantity, unitPrice? (siparişsiz, fiyat.alis ister), lots?, serials? }]. Maliyet = sipariş fiyatı (satır iskontosu sonrası) x mal kabul tarihi kuru. Karantina açıksa mal şubenin karantina deposuna girer. Siparişli kabulde bekleyen rezervasyonlar (expectedReceiptSourceId = sipariş) alıcı depoya taşınır. 409: over_delivery, receipt_exceeds_open, po_not_approved, po_cancelled; yanıtta warnings[] (over_delivery_within_tolerance). fiyat.alis yoksa fiyat alanları null döner.")]
    public static async Task<Created<ReceiptDto>> CreateReceipt(ISender sender, ReceiptInput input) {
        var receipt = await sender.Send(new CreateReceiptCommand(input));
        return TypedResults.Created($"/api/erp/purchasing/receipts/{receipt.Id}", receipt);
    }

    [EndpointSummary("Mal kabuller")]
    [EndpointDescription("satinalma.read. Süzgeçler: purchaseOrderId, supplierId, status, search, page, pageSize. fiyat.alis yoksa tutarlar null döner.")]
    public static async Task<Ok<ReceiptListResult>> ListReceipts(ISender sender, Guid? purchaseOrderId, Guid? supplierId, GoodsReceiptStatus? status, string? search,
                                                                 int page = 1, int pageSize = 25) {
        return TypedResults.Ok(await sender.Send(new ListReceiptsQuery(purchaseOrderId, supplierId, status, search, page, pageSize)));
    }

    [EndpointSummary("Mal kabul detayı")]
    [EndpointDescription("satinalma.read. fiyat.alis yoksa fiyat ve tutar alanları null döner.")]
    public static async Task<Ok<ReceiptDto>> GetReceipt(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetReceiptQuery(id)));
    }

    [EndpointSummary("Mal kabulü iptal et")]
    [EndpointDescription("satinalma.receive. Gövde: { reason }. Stok girişini tersler, sipariş gelen miktarını düşürür. 409: receipt_cancelled, receipt_invoiced (faturalanmış), receipt_processed (karantina/iade görmüş).")]
    public static async Task<Ok<ReceiptDto>> CancelReceipt(ISender sender, Guid id, ReasonBody body) {
        return TypedResults.Ok(await sender.Send(new CancelReceiptCommand(id, body.Reason)));
    }

    [EndpointSummary("Karantinadaki miktarlar")]
    [EndpointDescription("satinalma.read. Süzgeç: branchId. Mal kabul satırı başına karantinadaki açık miktar (openQuantity satır biriminde, openBaseQuantity temel birimde). Fiyat içermez.")]
    public static async Task<Ok<IReadOnlyList<QuarantineRowDto>>> ListQuarantine(ISender sender, Guid? branchId) {
        return TypedResults.Ok(await sender.Send(new ListQuarantineQuery(branchId)));
    }

    [EndpointSummary("Karantinadan serbest bırak")]
    [EndpointDescription("satinalma.quarantine. Gövde: { receiptLineId, quantity (satır biriminde), targetWarehouseId (normal depo) }. Açık miktar P3 transferiyle hedef depoya taşınır; siparişli kabulde bekleyen rezervasyonlar hedef depoya geçer. 409: quarantine_not_enabled, quarantine_not_held, quarantine_exceeds_open; 400 target_not_normal.")]
    public static async Task<Ok<QuarantineResultDto>> ReleaseQuarantine(ISender sender, ReleaseInput input) {
        return TypedResults.Ok(await sender.Send(new ReleaseQuarantineCommand(input)));
    }

    [EndpointSummary("Karantinadan reddet")]
    [EndpointDescription("satinalma.quarantine. Gövde: { receiptLineId, quantity (satır biriminde), action: \"damaged\" | \"return\" }. damaged: şubenin hasarlı deposuna transfer (409 damaged_warehouse_missing). return: tedarikçiye iade (stok çıkışı; mal kabule bağlı kesilmiş alış faturası varsa iade faturası taslağı, yanıtta returnId / invoiceId).")]
    public static async Task<Ok<QuarantineResultDto>> RejectQuarantine(ISender sender, RejectInput input) {
        return TypedResults.Ok(await sender.Send(new RejectQuarantineCommand(input)));
    }

    [EndpointSummary("Alış iadesi")]
    [EndpointDescription("satinalma.manage. Gövde: { receiptId, lines[{ receiptLineId, quantity (satır biriminde), lots?, serials? }], date, reason, createInvoice }. Stok çıkışı (PurchaseReturn) yazar; kaynak depo önce karantinadaki açık miktar, kalanı mal kabulün hedef deposudur. createInvoice: mal kabule bağlı kesilmiş alış faturasına bağlı iade faturası taslağı (AffectsStock=false); fatura yoksa 409 purchase_invoice_required. 400 return_exceeds_received.")]
    public static async Task<Ok<ReturnResultDto>> CreateReturn(ISender sender, ReturnInput input) {
        return TypedResults.Ok(await sender.Send(new CreateReturnCommand(input)));
    }
}
