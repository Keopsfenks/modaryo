using ERP.Application.Invoicing;
using ERP.Application.Sales.Invoicing;
using ERP.Application.Sales.Orders;
using ERP.Application.Sales.Recurring;
using ERP.Application.Sales.Targets;
using ERP.Application.Sales.Returns;
using ERP.Application.Sales.Shipments;
using ERP.Application.Shipping.Packing;
using ERP.Application.Shipping.Picking;
using ERP.Application.Sales.Quotes;
using ERP.Domain.Sales;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Sales : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/sales";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(SalesQuotes, "quotes");
        groupBuilder.MapPost(SalesCreateQuote, "quotes");
        groupBuilder.MapGet(SalesGetQuote, "quotes/{id:guid}");
        groupBuilder.MapPut(SalesUpdateQuote, "quotes/{id:guid}");
        groupBuilder.MapPost(SalesSendQuote, "quotes/{id:guid}/send");
        groupBuilder.MapPost(SalesAcceptQuote, "quotes/{id:guid}/accept");
        groupBuilder.MapPost(SalesRejectQuote, "quotes/{id:guid}/reject");
        groupBuilder.MapGet(SalesQuotePdf, "quotes/{id:guid}/pdf");

        groupBuilder.MapGet(SalesOrders, "orders");
        groupBuilder.MapGet(SalesPendingOrders, "orders/pending");
        groupBuilder.MapPost(SalesCreateOrder, "orders");
        groupBuilder.MapGet(SalesGetOrder, "orders/{id:guid}");
        groupBuilder.MapPut(SalesUpdateOrder, "orders/{id:guid}");
        groupBuilder.MapPost(SalesApproveOrder, "orders/{id:guid}/approve");
        groupBuilder.MapPost(SalesApproveLimit, "orders/{id:guid}/approve-limit");
        groupBuilder.MapPost(SalesCancelOrder, "orders/{id:guid}/cancel");
        groupBuilder.MapPost(SalesCloseOrder, "orders/{id:guid}/close");
        groupBuilder.MapPut(SalesSetPlan, "orders/{id:guid}/installment-plan");
        groupBuilder.MapGet(SalesOrderProforma, "orders/{id:guid}/proforma.pdf");
        groupBuilder.MapPost(SalesQuickInvoice, "orders/{id:guid}/quick-invoice");

        groupBuilder.MapGet(SalesShipments, "shipments");
        groupBuilder.MapPost(SalesCreateShipment, "shipments");
        groupBuilder.MapPost(SalesInvoiceDraft, "shipments/invoice-draft");
        groupBuilder.MapGet(SalesGetShipment, "shipments/{id:guid}");
        groupBuilder.MapPost(SalesDeliverShipment, "shipments/{id:guid}/deliver");
        groupBuilder.MapPost(SalesCancelShipment, "shipments/{id:guid}/cancel");
        groupBuilder.MapPost(SalesShipmentPickList, "shipments/{id:guid}/pick-list");
        groupBuilder.MapPut(SalesShipmentParcels, "shipments/{id:guid}/parcels");

        groupBuilder.MapGet(SalesReturns, "returns");
        groupBuilder.MapPost(SalesCreateReturn, "returns");
        groupBuilder.MapGet(SalesGetReturn, "returns/{id:guid}");
        groupBuilder.MapPost(SalesCancelReturn, "returns/{id:guid}/cancel");

        groupBuilder.MapGet(SalesRecurringList, "recurring");
        groupBuilder.MapPost(SalesCreateRecurring, "recurring");
        groupBuilder.MapGet(SalesGetRecurring, "recurring/{id:guid}");
        groupBuilder.MapPut(SalesUpdateRecurring, "recurring/{id:guid}");
        groupBuilder.MapPost(SalesPauseRecurring, "recurring/{id:guid}/pause");
        groupBuilder.MapPost(SalesResumeRecurring, "recurring/{id:guid}/resume");
        groupBuilder.MapGet(SalesRecurringRuns, "recurring/{id:guid}/runs");

        groupBuilder.MapGet(SalesTargets, "targets");
        groupBuilder.MapPut(SalesPutTargets, "targets");
        groupBuilder.MapGet(SalesTargetActuals, "targets/actuals");
    }

    [EndpointSummary("Siparişler")]
    [EndpointDescription("satis.read. Süzgeçler: status, currentAccountId, salesRepUserId, search (sipariş no / müşteri adı), page, pageSize.")]
    public static async Task<Ok<OrderListResult>> SalesOrders(ISender sender, SalesOrderStatus? status, Guid? currentAccountId, Guid? salesRepUserId, string? search,
                                                             int page = 1, int pageSize = 25) {
        return TypedResults.Ok(await sender.Send(new ListOrdersQuery(status, currentAccountId, salesRepUserId, search, page, pageSize)));
    }

    [EndpointSummary("Bekleyen sevk ve fatura")]
    [EndpointDescription("satis.read. Onaylı siparişlerin satırları: openToShip (sevk edilecek) ve openToInvoice (faturalanacak) miktarları. Süzgeçler: currentAccountId, search, toShip, toInvoice, page, pageSize.")]
    public static async Task<Ok<PendingListResult>> SalesPendingOrders(ISender sender, Guid? currentAccountId, string? search, bool? toShip, bool? toInvoice, int page = 1, int pageSize = 50) {
        return TypedResults.Ok(await sender.Send(new PendingOrdersQuery(currentAccountId, search, toShip, toInvoice, page, pageSize)));
    }

    [EndpointSummary("Sipariş oluştur")]
    [EndpointDescription("satis.manage. Gövde: currentAccountId, branchId, date, currency, salesRepUserId?, docDiscount?, notes?, requiresPrepayment?, prepaymentPercent? | prepaymentAmount?, lines[{ productId, variantId?, unit?, quantity, width?, length?, unitPrice?, vatIncluded?, discountPercent?, vatRate?, supplyMode? (stock|backToBack), supplierId?, warehouseId?, description? }]. 201 + sipariş (taslak).")]
    public static async Task<Created<OrderDto>> SalesCreateOrder(ISender sender, OrderInput input) {
        var order = await sender.Send(new CreateOrderCommand(input));
        return TypedResults.Created($"/api/erp/sales/orders/{order.Id}", order);
    }

    [EndpointSummary("Sipariş detayı")]
    [EndpointDescription("satis.read. Satırlar (tedarik zaman çizgisi timeline[], miktarlar), rezervasyonlar, sevkiyatlar, faturalar, taksit planı (kalan satırlar) ve avans tahsilatları. estimatedUnitCost / marginPercent yalnızca maliyet.read ile gelir.")]
    public static async Task<Ok<OrderDto>> SalesGetOrder(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetOrderQuery(id)));
    }

    [EndpointSummary("Siparişi güncelle")]
    [EndpointDescription("satis.manage. Yalnızca taslak (409 order_not_draft). version eşleşmezse 409 version_conflict.")]
    public static async Task<Ok<OrderDto>> SalesUpdateOrder(ISender sender, Guid id, OrderInput input) {
        return TypedResults.Ok(await sender.Send(new UpdateOrderCommand(id, input)));
    }

    [EndpointSummary("Siparişi onayla")]
    [EndpointDescription("satis.approve. Sırayla: modül (siparise-gore-tedarik; 403 module_disabled), kredi limiti (status awaitingLimitApproval), ön ödeme (awaitingPayment), stok (rezervasyon; 409 insufficient_stock, details = satırlar; eksi stok açıksa warnings). Siparişe göre tedarik satırları için tedarikçi başına taslak satın alma siparişi üretilir.")]
    public static async Task<Ok<OrderDto>> SalesApproveOrder(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new ApproveOrderCommand(id)));
    }

    [EndpointSummary("Limit aşımını onayla")]
    [EndpointDescription("satis.limit-asimi. awaitingLimitApproval durumundaki siparişi limit kontrolünü atlayarak ön ödeme ve stok adımlarına devam ettirir.")]
    public static async Task<Ok<OrderDto>> SalesApproveLimit(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new ApproveLimitCommand(id)));
    }

    [EndpointSummary("Siparişi iptal et")]
    [EndpointDescription("satis.manage. Gövde: { reason }. Rezervasyonları bırakır, taslak tedarik siparişlerini iptal eder. Sevk edilmiş siparişte 409 order_has_shipments.")]
    public static async Task<Ok<OrderDto>> SalesCancelOrder(ISender sender, Guid id, ReasonBody body) {
        return TypedResults.Ok(await sender.Send(new CancelOrderCommand(id, body.Reason)));
    }

    public sealed record OptionalReasonBody(string? Reason);

    [EndpointSummary("Siparişi kapat")]
    [EndpointDescription("satis.manage. Gövde: { reason? }. Kalan açık miktarın rezervasyonunu bırakır.")]
    public static async Task<Ok<OrderDto>> SalesCloseOrder(ISender sender, Guid id, OptionalReasonBody? body) {
        return TypedResults.Ok(await sender.Send(new CloseOrderCommand(id, body?.Reason)));
    }

    [EndpointSummary("Taksit planı")]
    [EndpointDescription("satis.manage. Gövde: { downPayment, rows[{ dueDate, amount }] } ya da { downPayment, count, firstDueDate } (eşit bölünür, kuruş artığı son satıra). Peşinat + taksitler = sipariş tutarı olmalıdır (400 installment_mismatch). Faturalarca kullanılmış plan değişmez (409 plan_consumed).")]
    public static async Task<Ok<OrderDto>> SalesSetPlan(ISender sender, Guid id, PlanInput input) {
        return TypedResults.Ok(await sender.Send(new SetInstallmentPlanCommand(id, input)));
    }

    [EndpointSummary("Proforma PDF")]
    [EndpointDescription("satis.read. Sipariş için proforma fatura PDF dosyası.")]
    public static async Task<FileContentHttpResult> SalesOrderProforma(ISender sender, Guid id) {
        var file = await sender.Send(new GetOrderProformaQuery(id));
        return TypedResults.File(file.Content, "application/pdf", file.FileName);
    }

    [EndpointSummary("Hızlı fatura")]
    [EndpointDescription("satis.ship + fatura.issue. Gövde: { warehouseId? }. Siparişin tüm açık miktarlarını sevk edip faturalayarak keser; tek işlemde (stok yetersizse hepsi geri alınır). Sonuç: { shipmentId, invoiceId, invoice }.")]
    public static async Task<Ok<QuickInvoiceResult>> SalesQuickInvoice(ISender sender, Guid id, QuickInvoiceInput? input) {
        return TypedResults.Ok(await sender.Send(new QuickInvoiceCommand(id, input?.WarehouseId)));
    }

    [EndpointSummary("Sevkler")]
    [EndpointDescription("satis.read. Süzgeçler: status, orderId, currentAccountId, search (sevk no / sipariş no / müşteri), page, pageSize.")]
    public static async Task<Ok<ShipmentListResult>> SalesShipments(ISender sender, ShipmentStatus? status, Guid? orderId, Guid? currentAccountId, string? search, int page = 1, int pageSize = 25) {
        return TypedResults.Ok(await sender.Send(new ListShipmentsQuery(status, orderId, currentAccountId, search, page, pageSize)));
    }

    [EndpointSummary("Sevk oluştur")]
    [EndpointDescription("satis.ship. Gövde: { orderId, warehouseId?, date?, deliveryAddressId?, cargoNote?, notes?, draft?, lines[{ orderLineId, quantity, lots?, serials? }] }. draft true ise stok çıkışı yapılmadan taslak sevk (201, durum draft) oluşturulur; toplama-paketleme akışı buradan başlar. Rezervasyonu tüketir, SalesShipment stok çıkışı yazar; sipariş kısmen/tamamen sevk edilmiş olur. 400 ship_exceeds_open, 409 supply_pending (siparişe göre tedarik henüz gelmedi). 201 + sevk (durum shipped).")]
    public static async Task<Created<ShipmentDto>> SalesCreateShipment(ISender sender, ShipmentInput input) {
        var shipment = await sender.Send(new CreateShipmentCommand(input));
        return TypedResults.Created($"/api/erp/sales/shipments/{shipment.Id}", shipment);
    }

    [EndpointSummary("Sevk detayı")]
    [EndpointDescription("satis.read. Satırlar, teslimat adresi (sevk anında cari adreslerinden dondurulur), faturalanan miktarlar.")]
    public static async Task<Ok<ShipmentDto>> SalesGetShipment(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetShipmentQuery(id)));
    }

    [EndpointSummary("Teslim edildi")]
    [EndpointDescription("satis.ship. Gövde: { deliveredAt?, receivedBy? }. shipped -> delivered.")]
    public static async Task<Ok<ShipmentDto>> SalesDeliverShipment(ISender sender, Guid id, DeliverInput? input) {
        return TypedResults.Ok(await sender.Send(new DeliverShipmentCommand(id, input ?? new DeliverInput(null, null))));
    }

    [EndpointSummary("Sevki iptal et")]
    [EndpointDescription("satis.ship. Gövde: { reason }. Stok çıkışı geri alınır, rezervasyon yeniden aktif olur. Faturaya bağlı sevk 409 shipment_invoiced.")]
    public static async Task<Ok<ShipmentDto>> SalesCancelShipment(ISender sender, Guid id, ReasonBody body) {
        return TypedResults.Ok(await sender.Send(new CancelShipmentCommand(id, body.Reason)));
    }

    [EndpointSummary("Sevk için toplama listesi")]
    [EndpointDescription("sevkiyat.pick. Taslak sevk için FEFO lot önerili toplama listesi oluşturur; sevk picking olur. Toplu liste: POST /api/erp/shipping/pick-lists/batch.")]
    public static async Task<Created<PickListDto>> SalesShipmentPickList(ISender sender, Guid id) {
        var list = await sender.Send(new CreatePickListCommand(id));
        return TypedResults.Created($"/api/erp/shipping/pick-lists/{list.Id}", list);
    }

    [EndpointSummary("Sevk kolileri")]
    [EndpointDescription("sevkiyat.pack. Gövde: { parcels[{ weightKg, widthCm, lengthCm, heightCm, contents[{ shipmentLineId, qty }] }] }. Kolileri değiştirir (desi = en x boy x yükseklik / 3000, 0,1'e yukarı) ve sevki packed yapar. 409 parcel_required | pick_incomplete | invalid_shipment_transition, 400 parcel_contents_mismatch.")]
    public static async Task<Ok<ShipmentDto>> SalesShipmentParcels(ISender sender, Guid id, PackInput input) {
        return TypedResults.Ok(await sender.Send(new PackShipmentCommand(id, input)));
    }

    [EndpointSummary("Sevklerden fatura taslağı")]
    [EndpointDescription("satis.manage + fatura.manage. Gövde: { shipmentIds[], date? } (aynı sipariş). Faturalanmamış sevk miktarlarıyla satış faturası taslağı; fiyat/iskonto/KDV siparişten donmuş gelir, vade planı siparişin taksit planından önizlenir (kesimde tüketilir). 409 nothing_to_invoice.")]
    public static async Task<Created<InvoiceDto>> SalesInvoiceDraft(ISender sender, InvoiceFromShipmentsInput input) {
        var invoice = await sender.Send(new CreateInvoiceFromShipmentsCommand(input));
        return TypedResults.Created($"/api/erp/invoices/{invoice.Id}", invoice);
    }

    [EndpointSummary("İadeler")]
    [EndpointDescription("satis.read. Süzgeçler: status, invoiceId, currentAccountId, page, pageSize.")]
    public static async Task<Ok<ReturnListResult>> SalesReturns(ISender sender, SalesReturnStatus? status, Guid? invoiceId, Guid? currentAccountId, int page = 1, int pageSize = 25) {
        return TypedResults.Ok(await sender.Send(new ListReturnsQuery(status, invoiceId, currentAccountId, page, pageSize)));
    }

    [EndpointSummary("İade oluştur")]
    [EndpointDescription("satis.return. Gövde: { invoiceId, warehouseKind (normal|quarantine|damaged), warehouseId, date?, reason?, lines[{ invoiceLineId, quantity, lots?, serials? }] }. Stok girişi (SalesReturn, asıl çıkış maliyetiyle) yazar ve SalesReturn fatura taslağı üretir. 400 return_exceeds_invoiced, warehouse_kind_mismatch.")]
    public static async Task<Created<ReturnDto>> SalesCreateReturn(ISender sender, ReturnInput input) {
        var created = await sender.Send(new CreateReturnCommand(input));
        return TypedResults.Created($"/api/erp/sales/returns/{created.Id}", created);
    }

    [EndpointSummary("İade detayı")]
    [EndpointDescription("satis.read.")]
    public static async Task<Ok<ReturnDto>> SalesGetReturn(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetReturnQuery(id)));
    }

    [EndpointSummary("İadeyi iptal et")]
    [EndpointDescription("satis.return. Gövde: { reason }. Taslak iade faturası silinir, stok girişi geri alınır. Kesilmiş iade faturası varsa 409 return_invoice_issued (önce faturayı iptal edin).")]
    public static async Task<Ok<ReturnDto>> SalesCancelReturn(ISender sender, Guid id, ReasonBody body) {
        return TypedResults.Ok(await sender.Send(new CancelReturnCommand(id, body.Reason)));
    }

    [EndpointSummary("Teklifler")]
    [EndpointDescription("satis.read. Süzgeçler: status, currentAccountId, salesRepUserId, search (teklif no / müşteri adı), page, pageSize. Satırda geçerli revizyonun tutarı bulunur.")]
    public static async Task<Ok<QuoteListResult>> SalesQuotes(ISender sender, QuoteStatus? status, Guid? currentAccountId, Guid? salesRepUserId, string? search,
                                                             int page = 1, int pageSize = 25) {
        return TypedResults.Ok(await sender.Send(new ListQuotesQuery(status, currentAccountId, salesRepUserId, search, page, pageSize)));
    }

    [EndpointSummary("Teklif oluştur")]
    [EndpointDescription("satis.manage. Gövde: currentAccountId, branchId, date, validUntil? (yoksa ayardaki geçerlilik günü), currency, salesRepUserId? (yoksa carinin temsilcisi), docDiscount? (belge iskonto yüzdesi), notes?, lines[{ productId, variantId?, unit?, quantity, width?, length?, unitPrice?, vatIncluded?, discountPercent?, vatRate?, supplyMode? (stock|backToBack), description?, supplierId? }]. unitPrice verilmezse satış fiyatı önceliğiyle (müşteri özel, grup listesi, varsayılan liste, kart) belirlenir. Ölçülü kalemde en x boy (m) zorunlu, miktar en x boy. 201 + teklif. 400 sales_rep_required / sales_rep_invalid / price_required; 403 discount_limit_exceeded.")]
    public static async Task<Created<QuoteDto>> SalesCreateQuote(ISender sender, QuoteInput input) {
        var quote = await sender.Send(new CreateQuoteCommand(input));
        return TypedResults.Created($"/api/erp/sales/quotes/{quote.Id}", quote);
    }

    [EndpointSummary("Teklif detayı")]
    [EndpointDescription("satis.read. revision verilmezse geçerli revizyon; tüm revizyonların özeti revisions[] içindedir.")]
    public static async Task<Ok<QuoteDto>> SalesGetQuote(ISender sender, Guid id, int? revision) {
        return TypedResults.Ok(await sender.Send(new GetQuoteQuery(id, revision)));
    }

    [EndpointSummary("Teklifi güncelle")]
    [EndpointDescription("satis.manage. Taslak: yerinde güncellenir. Gönderilmiş (Sent): yeni revizyon açılır, eski revizyon satırları salt okunur kalır ve teklif taslağa döner. Diğer durumlarda 409 quote_not_editable. version eşleşmezse 409 version_conflict.")]
    public static async Task<Ok<QuoteDto>> SalesUpdateQuote(ISender sender, Guid id, QuoteInput input) {
        return TypedResults.Ok(await sender.Send(new UpdateQuoteCommand(id, input)));
    }

    public sealed record SendBody(List<string>? EmailTo, List<string>? Cc, string? Subject, string? Body);

    [EndpointSummary("Teklifi gönder")]
    [EndpointDescription("satis.manage. Gövde: { emailTo[]?, cc[]?, subject?, body? }. Taslak -> Gönderildi; emailTo verilirse teklif PDF eki ile e-postalanır. 409 quote_not_editable.")]
    public static async Task<Ok<QuoteDto>> SalesSendQuote(ISender sender, Guid id, SendBody body) {
        return TypedResults.Ok(await sender.Send(new SendQuoteCommand(id, body.EmailTo, body.Cc, body.Subject, body.Body)));
    }

    [EndpointSummary("Teklifi kabul et")]
    [EndpointDescription("satis.manage. Gönderilmiş teklif -> Kabul; aynı satırlarla taslak sipariş üretilir. Yanıt: { orderId, orderNumber }. 409 quote_not_editable (gönderilmemiş), quote_expired.")]
    public static async Task<Ok<AcceptQuoteResult>> SalesAcceptQuote(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new AcceptQuoteCommand(id)));
    }

    public sealed record ReasonBody(string Reason);

    [EndpointSummary("Teklifi reddet")]
    [EndpointDescription("satis.manage. Gövde: { reason }. Yalnızca gönderilmiş teklif reddedilebilir (409 quote_not_editable).")]
    public static async Task<Ok<QuoteDto>> SalesRejectQuote(ISender sender, Guid id, ReasonBody body) {
        return TypedResults.Ok(await sender.Send(new RejectQuoteCommand(id, body.Reason)));
    }

    [EndpointSummary("Teklif PDF dosyası")]
    [EndpointDescription("satis.read. A4 PDF; revision verilmezse geçerli revizyon. Ölçülü satırlarda ebat (1,60 × 2,30) basılır.")]
    public static async Task<FileContentHttpResult> SalesQuotePdf(ISender sender, Guid id, int? revision) {
        var file = await sender.Send(new GetQuotePdfQuery(id, revision));
        return TypedResults.File(file.Content, "application/pdf", file.FileName);
    }

    [EndpointSummary("Tekrarlayan fatura şablonları")]
    [EndpointDescription("satis.recurring. active? ve currentAccountId? süzgeçleri. Her satırda nextPeriod (üretilmemiş ilk dönem) ve lastPeriod bulunur.")]
    public static async Task<Ok<IReadOnlyList<RecurringListItem>>> SalesRecurringList(ISender sender, bool? active, Guid? currentAccountId) {
        return TypedResults.Ok(await sender.Send(new ListRecurringQuery(active, currentAccountId)));
    }

    [EndpointSummary("Tekrarlayan fatura şablonu oluştur")]
    [EndpointDescription("satis.recurring. Gövde: { name?, currentAccountId, branchId, currency, interval (monthly|quarterly|yearly), dayOfMonth (1-28), startDate, endDate?, mode (draft|autoIssue), notes?, lines[{ productId?, variantId?, description?, unit?, quantity, unitPrice, vatIncluded, discountPercent?, vatRate?, fixedPrice (varsayılan true) }] }. Numara AB0001. fixedPrice=false olan kalemli satırlar üretim gününde fiyat listesinden yeniden fiyatlanır. Günlük iş (06:00 İstanbul) bugüne kadar üretilmemiş dönemlerin faturasını oluşturur; dönem başına tek fatura. 201 + şablon.")]
    public static async Task<Created<RecurringDto>> SalesCreateRecurring(ISender sender, RecurringInput input) {
        var template = await sender.Send(new CreateRecurringCommand(input));
        return TypedResults.Created($"/api/erp/sales/recurring/{template.Id}", template);
    }

    [EndpointSummary("Tekrarlayan fatura şablonu detayı")]
    [EndpointDescription("satis.recurring.")]
    public static async Task<Ok<RecurringDto>> SalesGetRecurring(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetRecurringQuery(id)));
    }

    [EndpointSummary("Tekrarlayan fatura şablonunu güncelle")]
    [EndpointDescription("satis.recurring. Değişiklik yalnızca sonraki üretimleri etkiler; üretilmiş faturalara dokunulmaz.")]
    public static async Task<Ok<RecurringDto>> SalesUpdateRecurring(ISender sender, Guid id, RecurringInput input) {
        return TypedResults.Ok(await sender.Send(new UpdateRecurringCommand(id, input)));
    }

    [EndpointSummary("Şablonu duraklat")]
    [EndpointDescription("satis.recurring. Duraklatılan şablon fatura üretmez ve nakit akışı tahminine girmez.")]
    public static async Task<Ok<RecurringDto>> SalesPauseRecurring(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new SetRecurringActiveCommand(id, false)));
    }

    [EndpointSummary("Şablonu sürdür")]
    [EndpointDescription("satis.recurring. Duraklama sırasında atlanan dönemler (bitiş tarihine kadar) sonraki günlük işte geriye dönük üretilir.")]
    public static async Task<Ok<RecurringDto>> SalesResumeRecurring(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new SetRecurringActiveCommand(id, true)));
    }

    [EndpointSummary("Şablonun ürettiği faturalar")]
    [EndpointDescription("satis.recurring. Dönem sırasıyla; invoiceStatus draft|issued|cancelled, taslağı silinmişse deleted.")]
    public static async Task<Ok<IReadOnlyList<RecurringRunDto>>> SalesRecurringRuns(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new ListRecurringRunsQuery(id)));
    }

    public sealed record TargetsBody(IReadOnlyList<TargetRowInput> Rows);

    [EndpointSummary("Satış hedefleri")]
    [EndpointDescription("satis.targets. year verilmezse içinde bulunulan yıl. Satırlar: temsilci x ay hedefi (amountTry).")]
    public static async Task<Ok<TargetsDto>> SalesTargets(ISender sender, int? year) {
        return TypedResults.Ok(await sender.Send(new GetTargetsQuery(year)));
    }

    [EndpointSummary("Satış hedeflerini kaydet")]
    [EndpointDescription("satis.targets. Gövde: { rows[{ userId, month (1-12), amountTry }] }. Yılın tüm hedefleri bu satırlarla değiştirilir. 400 sales_rep_invalid (üye değil).")]
    public static async Task<Ok<TargetsDto>> SalesPutTargets(ISender sender, int year, TargetsBody body) {
        return TypedResults.Ok(await sender.Send(new PutTargetsCommand(year, body.Rows)));
    }

    [EndpointSummary("Hedef ve gerçekleşen")]
    [EndpointDescription("satis.targets. Temsilci x ay: targetTry, actualTry (kesilmiş satış faturalarının KDV hariç TL net tutarı - kesilmiş iade faturaları), percent. Temsilci: sevkten kesilen faturada siparişin, iade belgesinde siparişin ya da asıl faturanın, diğerlerinde carinin varsayılan temsilcisi.")]
    public static async Task<Ok<TargetActualsDto>> SalesTargetActuals(ISender sender, int? year) {
        return TypedResults.Ok(await sender.Send(new GetTargetActualsQuery(year)));
    }
}
