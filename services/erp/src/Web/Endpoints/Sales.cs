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
