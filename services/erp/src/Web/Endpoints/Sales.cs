using ERP.Application.Sales.Orders;
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
}
