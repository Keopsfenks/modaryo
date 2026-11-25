using ERP.Application.Invoicing;
using ERP.Application.Purchasing.Invoices;
using ERP.Application.Purchasing.Invoices.Commands;
using ERP.Application.Purchasing.ImportFiles;
using ERP.Application.Purchasing.LandedCosts;
using ERP.Application.Purchasing.Orders;
using ERP.Application.Purchasing.Quarantine;
using ERP.Application.Purchasing.Returns;
using ERP.Application.Purchasing.Receipts;
using ERP.Application.Purchasing.Suggestions;
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
        groupBuilder.MapPost(CreateInvoiceDraft, "receipts/invoice-draft");
        groupBuilder.MapGet(PriceStats, "price-stats");
        groupBuilder.MapPost(CreateLandedCost, "landed-costs");
        groupBuilder.MapGet(ListLandedCosts, "landed-costs");
        groupBuilder.MapGet(GetLandedCost, "landed-costs/{id:guid}");
        groupBuilder.MapPost(PostLandedCost, "landed-costs/{id:guid}/post");
        groupBuilder.MapPost(CancelLandedCost, "landed-costs/{id:guid}/cancel");
        groupBuilder.MapPost(CreateImportFile, "import-files");
        groupBuilder.MapGet(ListImportFiles, "import-files");
        groupBuilder.MapGet(GetImportFile, "import-files/{id:guid}");
        groupBuilder.MapPut(UpdateImportFile, "import-files/{id:guid}");
        groupBuilder.MapPost(LinkImportFile, "import-files/{id:guid}/link");
        groupBuilder.MapPost(CloseImportFile, "import-files/{id:guid}/close");
        groupBuilder.MapGet(ImportSummary, "import-files/{id:guid}/summary");
        groupBuilder.MapGet(ListQuarantine, "quarantine");
        groupBuilder.MapPost(ReleaseQuarantine, "quarantine/release");
        groupBuilder.MapPost(RejectQuarantine, "quarantine/reject");
        groupBuilder.MapPost(CreateReturn, "returns");
        groupBuilder.MapGet(Suggestions, "suggestions");
        groupBuilder.MapPost(CreateSuggestionOrders, "suggestions/create-orders");
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

    [EndpointSummary("Mal kabullerden alış faturası taslağı")]
    [EndpointDescription("satinalma.manage + fatura.manage + fiyat.alis. Gövde: { receiptIds[], externalNumber, date? }. Mal kabullerin henüz faturalanmamış miktarlarından P4 alış faturası taslağı (kaynak purchase-receipt; stok etkilemez). Fiyat sipariş satırından gelir; kur fatura tarihinde çözülür. 409 nothing_to_invoice, receipt_cancelled; 400 supplier_mismatch, currency_mismatch. Fatura kesilince maliyet farkı mal kabul tarihine düzeltme olarak yazılır, iptalde geri alınır.")]
    public static async Task<Created<InvoiceDto>> CreateInvoiceDraft(ISender sender, InvoiceDraftInput input) {
        var invoice = await sender.Send(new CreateInvoiceFromReceiptsCommand(input));
        return TypedResults.Created($"/api/erp/invoices/{invoice.Id}", invoice);
    }

    [EndpointSummary("Alış fiyat takibi")]
    [EndpointDescription("satinalma.read + fiyat.alis. Süzgeçler: productId, variantId. Kesilmiş alış faturalarından kalem ve birim başına son, en düşük ve en yüksek TL birim fiyat (12 ay) ve son alış tarihi.")]
    public static async Task<Ok<IReadOnlyList<PriceStatDto>>> PriceStats(ISender sender, Guid? productId, Guid? variantId) {
        return TypedResults.Ok(await sender.Send(new GetPriceStatsQuery(productId, variantId)));
    }

    [EndpointSummary("Masraf dağıtımı oluştur")]
    [EndpointDescription("satinalma.manage + fiyat.alis. Gövde: { date, key: amount|quantity|weight, receiptIds[]?, importFileId?, notes?, sources[{ invoiceId? | manualAmountTl, description?, costType: navlun|gumruk|sigorta|diger }] }. Hedefler mal kabul satırlarıdır (ithalat dosyasına bağlı mal kabuller dahil). Her kaynak kuruşu kuruşuna dağıtılır. 409 weight_missing (ağırlık anahtarında ağırlığı olmayan kalemler mesajda), landed_cost_target_cancelled, landed_cost_source_cancelled, invoice_already_allocated. Taslak olarak oluşur; /post ile maliyet düzeltmesi yazılır.")]
    public static async Task<Created<LandedCostDto>> CreateLandedCost(ISender sender, LandedCostInput input) {
        var landed = await sender.Send(new CreateLandedCostCommand(input));
        return TypedResults.Created($"/api/erp/purchasing/landed-costs/{landed.Id}", landed);
    }

    [EndpointSummary("Masraf dağıtımları")]
    [EndpointDescription("satinalma.read + fiyat.alis. Süzgeçler: status, importFileId.")]
    public static async Task<Ok<IReadOnlyList<LandedCostListItemDto>>> ListLandedCosts(ISender sender, LandedCostStatus? status, Guid? importFileId) {
        return TypedResults.Ok(await sender.Send(new ListLandedCostsQuery(status, importFileId)));
    }

    [EndpointSummary("Masraf dağıtımı detayı")]
    [EndpointDescription("satinalma.read + fiyat.alis. Mal kabul satırı başına TL dağıtım tablosu ve masraf türü kırılımı.")]
    public static async Task<Ok<LandedCostDto>> GetLandedCost(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetLandedCostQuery(id)));
    }

    [EndpointSummary("Masraf dağıtımını işle")]
    [EndpointDescription("satinalma.manage + fiyat.alis. Her hedef satırın payı mal kabul tarihine P3 maliyet düzeltmesi olarak yazılır (sonraki çıkışlar işçi tarafından yeniden maliyetlenir). Dönem kilidi mal kabul tarihlerinde geçerlidir.")]
    public static async Task<Ok<LandedCostDto>> PostLandedCost(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new PostLandedCostCommand(id)));
    }

    [EndpointSummary("Masraf dağıtımını iptal et")]
    [EndpointDescription("satinalma.manage + fiyat.alis. Gövde: { reason }. İşlenmişse maliyet düzeltmeleri ters yönde yazılır.")]
    public static async Task<Ok<LandedCostDto>> CancelLandedCost(ISender sender, Guid id, ReasonBody body) {
        return TypedResults.Ok(await sender.Send(new CancelLandedCostCommand(id, body.Reason)));
    }

    [EndpointSummary("İthalat dosyası oluştur")]
    [EndpointDescription("satinalma.manage, ithalat modülü. Gövde: { supplierId, originCountry, incoterm: EXW|FCA|FOB|CIF|DAP|DDP, currency, notes? }. Numara IT{yyyy}{0001}.")]
    public static async Task<Created<ImportFileDto>> CreateImportFile(ISender sender, ImportFileInput input) {
        var file = await sender.Send(new CreateImportFileCommand(input));
        return TypedResults.Created($"/api/erp/purchasing/import-files/{file.Id}", file);
    }

    [EndpointSummary("İthalat dosyaları")]
    [EndpointDescription("satinalma.read, ithalat modülü. Süzgeç: status.")]
    public static async Task<Ok<IReadOnlyList<ImportFileListItemDto>>> ListImportFiles(ISender sender, ImportFileStatus? status) {
        return TypedResults.Ok(await sender.Send(new ListImportFilesQuery(status)));
    }

    [EndpointSummary("İthalat dosyası detayı")]
    [EndpointDescription("satinalma.read, ithalat modülü. Bağlı belgeler links[] içinde.")]
    public static async Task<Ok<ImportFileDto>> GetImportFile(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetImportFileQuery(id)));
    }

    [EndpointSummary("İthalat dosyasını güncelle")]
    [EndpointDescription("satinalma.manage, ithalat modülü. Kapalı dosya değiştirilemez (409 import_file_closed).")]
    public static async Task<Ok<ImportFileDto>> UpdateImportFile(ISender sender, Guid id, ImportFileInput input) {
        return TypedResults.Ok(await sender.Send(new UpdateImportFileCommand(id, input)));
    }

    [EndpointSummary("İthalat dosyasına belge bağla")]
    [EndpointDescription("satinalma.manage, ithalat modülü. Gövde: { kind: po|receipt|invoice|landedCost|attachment, id }. attachment için id P0b dosya kimliğidir.")]
    public static async Task<Ok<ImportFileDto>> LinkImportFile(ISender sender, Guid id, ImportLinkInput input) {
        return TypedResults.Ok(await sender.Send(new LinkImportFileCommand(id, input)));
    }

    [EndpointSummary("İthalat dosyasını kapat")]
    [EndpointDescription("satinalma.manage, ithalat modülü. Kapalı dosyaya belge bağlanamaz ve masraf dağıtılamaz.")]
    public static async Task<Ok<ImportFileDto>> CloseImportFile(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new CloseImportFileCommand(id)));
    }

    [EndpointSummary("İthalat dosyası özeti")]
    [EndpointDescription("satinalma.read + fiyat.alis, ithalat modülü. Mal değeri, masraf türüne göre işlenmiş masraflar ve kalem başına toplam birim maliyet.")]
    public static async Task<Ok<ImportSummaryDto>> ImportSummary(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetImportSummaryQuery(id)));
    }

    [EndpointSummary("Sipariş önerileri")]
    [EndpointDescription("satinalma.read. Süzgeç: branchId. Minimum stoğun altındaki kalemler varsayılan tedarikçiye göre gruplanır; tedarikçisiz kalemler supplierId null olan Tedarikçisiz grubunda gelir. Açık siparişlerdeki (onaylı, gelmemiş) miktar onOrder olarak düşülür. Önerilen miktar max(sipariş miktarı, minimum - (kullanılabilir + yolda)). lastPrice yalnızca fiyat.alis ile döner.")]
    public static async Task<Ok<SuggestionResult>> Suggestions(ISender sender, Guid? branchId) {
        return TypedResults.Ok(await sender.Send(new GetSuggestionsQuery(branchId)));
    }

    [EndpointSummary("Önerilerden sipariş oluştur")]
    [EndpointDescription("satinalma.manage + fiyat.alis. Gövde: { groups[{ supplierId, lines[{ productId, variantId?, warehouseId, quantity }] }] }. Tedarikçi (ve depo şubesi) başına bir taslak sipariş döner; fiyatlar alış fiyat önceliğinden gelir. 201 + siparişler.")]
    public static async Task<Created<IReadOnlyList<PoDto>>> CreateSuggestionOrders(ISender sender, CreateSuggestionOrdersInput input) {
        var orders = await sender.Send(new CreateSuggestionOrdersCommand(input));
        return TypedResults.Created("/api/erp/purchasing/orders", orders);
    }
}
