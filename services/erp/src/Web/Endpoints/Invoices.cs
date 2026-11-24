using ERP.Application.Invoicing;
using ERP.Application.Invoicing.Commands;
using ERP.Application.Invoicing.Documents;
using ERP.Application.Invoicing.Queries;
using ERP.Domain.Invoicing;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Invoices : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/invoices";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(List, "");
        groupBuilder.MapPost(Create, "");
        groupBuilder.MapPost(Preview, "preview");
        groupBuilder.MapGet(Get, "{id:guid}");
        groupBuilder.MapPut(Update, "{id:guid}");
        groupBuilder.MapDelete(Delete, "{id:guid}");
        groupBuilder.MapPost(Issue, "{id:guid}/issue");
        groupBuilder.MapPost(Cancel, "{id:guid}/cancel");
        groupBuilder.MapGet(Pdf, "{id:guid}/pdf");
        groupBuilder.MapPost(Email, "{id:guid}/email");
    }

    [EndpointSummary("Faturalar")]
    [EndpointDescription("fatura.read. Süzgeçler: kind, status, currentAccountId, from, to, search (numara/tedarikçi no/cari adı), branchId, page, pageSize. Yanıt: items, total, totals (iptaller hariç TL toplamlar). fiyat.alis yoksa alış faturaları listelenmez.")]
    public static async Task<Ok<InvoiceListResult>> List(ISender sender, InvoiceKind? kind, InvoiceStatus? status, Guid? currentAccountId, DateOnly? from,
                                                         DateOnly? to, string? search, Guid? branchId, int page = 1, int pageSize = 25) {
        return TypedResults.Ok(await sender.Send(new ListInvoicesQuery(kind, status, currentAccountId, from, to, search, branchId, page, pageSize)));
    }

    [EndpointSummary("Fatura taslağı oluştur")]
    [EndpointDescription("fatura.manage (alış türleri ayrıca fiyat.alis). Gövde InvoiceInput; toplamlar sunucuda hesaplanır. 201 + taslak fatura. Satır: productId ya da description, unit, quantity (ölçüyle satılan kalemlerde en x boy / boy hesaplanır), unitPrice, vatIncluded, discountPercent|discountAmount, vatRate, withholdingCode | exemptionCode (+%0 KDV), otvPercent|otvPerUnit, sourceLineId (iade). Yabancı para biriminde rate verilmezse TCMB kuru kullanılır (409 rate_missing).")]
    public static async Task<Created<InvoiceDto>> Create(ISender sender, InvoiceInput input) {
        var invoice = await sender.Send(new CreateDraftCommand(input));
        return TypedResults.Created($"/api/erp/invoices/{invoice.Id}", invoice);
    }

    [EndpointSummary("Toplam önizleme")]
    [EndpointDescription("fatura.read. Kaydetmeden aynı hesabı yapar: satır sonuçları, toplamlar, TL karşılıkları, KDV özeti ve cari para birimindeki tutar.")]
    public static async Task<Ok<PreviewDto>> Preview(ISender sender, InvoiceInput input) {
        return TypedResults.Ok(await sender.Send(new PreviewTotalsQuery(input)));
    }

    [EndpointSummary("Fatura detayı")]
    [EndpointDescription("fatura.read (alış türleri ayrıca fiyat.alis).")]
    public static async Task<Ok<InvoiceDto>> Get(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetInvoiceQuery(id)));
    }

    [EndpointSummary("Taslağı güncelle")]
    [EndpointDescription("fatura.manage. Gövde InvoiceInput; version verilirse eşleşmeyen 409 version_conflict. Yalnızca taslak: 409 invoice_not_draft (iptalde invoice_cancelled).")]
    public static async Task<Ok<InvoiceDto>> Update(ISender sender, Guid id, InvoiceInput input) {
        return TypedResults.Ok(await sender.Send(new UpdateDraftCommand(id, input)));
    }

    [EndpointSummary("Taslağı sil")]
    [EndpointDescription("fatura.manage. Yalnızca taslak; aksi halde 409 invoice_not_draft.")]
    public static async Task<NoContent> Delete(ISender sender, Guid id) {
        await sender.Send(new DeleteDraftCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Faturayı kes")]
    [EndpointDescription("fatura.issue. Dönem kilidini denetler (409 period_locked), modu (kâğıt / e-Fatura / e-Arşiv) ayarlardan ve cari işaretinden belirler, varsayılan seriden boşluksuz numara atar (seri yoksa 409 series_missing), alıcı/satıcı adresini dondurur.")]
    public static async Task<Ok<InvoiceDto>> Issue(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new IssueInvoiceCommand(id)));
    }

    public sealed record CancelBody(string Reason);

    [EndpointSummary("Faturayı iptal et")]
    [EndpointDescription("fatura.cancel (alış türleri ayrıca fiyat.alis). Gövde: { reason } (2-200 karakter). Defter hareketi, vade satırları, eşleştirmeler ve stok tek işlemde geri alınır; numara kullanılmış kalır. 409: invoice_cancelled (zaten iptal), invoice_not_issued (taslak), invoice_has_payments, period_locked.")]
    public static async Task<NoContent> Cancel(ISender sender, Guid id, CancelBody body) {
        await sender.Send(new CancelInvoiceCommand(id, body.Reason));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Fatura PDF'i")]
    [EndpointDescription("fatura.read (alış türleri ayrıca fiyat.alis). A4 PDF; firma logosu, alıcı/satıcı, satırlar, KDV özeti, tevkifat, yazıyla tutar ve (ayara göre) IBAN'lar. Taslakta numarasız ve TASLAK filigranlı, iptalde İPTAL filigranlıdır.")]
    public static async Task<FileContentHttpResult> Pdf(ISender sender, Guid id) {
        var file = await sender.Send(new GetInvoicePdfQuery(id));
        return TypedResults.File(file.Content, "application/pdf", file.FileName);
    }

    public sealed record EmailBody(List<string> To, List<string>? Cc, string? Subject, string? Body);

    [EndpointSummary("Faturayı e-posta ile gönder")]
    [EndpointDescription("fatura.issue (alış türleri ayrıca fiyat.alis). Gövde: { to[], cc[]?, subject?, body? }. Konu/metin verilmezse belge ayarlarındaki şablon kullanılır; {firma} {belgeNo} {alici} {tutar} {vade} değişkenleri doldurulur, metin düz metin sayılır. Fatura PDF olarak eklenir. 409: invoice_not_issued (taslak), invoice_cancelled; 502 mail_send_failed.")]
    public static async Task<NoContent> Email(ISender sender, Guid id, EmailBody body) {
        await sender.Send(new SendInvoiceEmailCommand(id, body.To, body.Cc, body.Subject, body.Body));
        return TypedResults.NoContent();
    }
}
