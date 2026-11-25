using System.Text.Json;
using ERP.Web.FunctionalTests.Invoicing;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Purchasing.PurchasingTestSupport;

namespace ERP.Web.FunctionalTests.Purchasing;

public class OrderPdfTests : TestBase {
    private const string Mailpit = "http://localhost:8025/api/v1";

    private static readonly HttpClient Http = new();

    private static async Task<byte[]> GetPdf(HttpClient client, Guid id) {
        using var response = await client.GetAsync($"{Api}/orders/{id}/pdf");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static async Task<JsonElement?> FindMessage(string subject) {
        for (var attempt = 0; attempt < 25; attempt++) {
            var list = JsonDocument.Parse(await Http.GetStringAsync($"{Mailpit}/messages?limit=200")).RootElement;
            foreach (var message in list.GetProperty("messages").EnumerateArray())
                if (message.GetProperty("Subject").GetString() == subject) {
                    var id = message.GetProperty("ID").GetString();
                    return JsonDocument.Parse(await Http.GetStringAsync($"{Mailpit}/message/{id}")).RootElement.Clone();
                }

            await Task.Delay(200);
        }

        return null;
    }

    [Test]
    public async Task PdfContainsLinesAndExpectedDates() {
        var env = await NewPurchasingEnv();
        var po  = await CreatePo(env, PoBody(env, [PoLine(env.Product, 10m, 20m, new { expectedDate = "2026-04-01" }),
                                                   PoLine(env.Product, 5m, 40m, new { expectedDate = "2026-04-15" })]));
        var id = po.GetProperty("id").GetGuid();
        await Approve(env.Client, id);

        var pdf  = await GetPdf(env.Client, id);
        var text = PdfTests.Text(pdf);

        pdf.AsSpan(0, 5).ToArray().ShouldBe("%PDF-"u8.ToArray());
        text.ShouldContain(po.GetProperty("number").GetString()!);
        text.ShouldContain("SATINALMASİPARİŞİ");
        text.ShouldContain(po.GetProperty("lines")[0].GetProperty("productName").GetString()!.Replace(" ", ""));
        text.ShouldContain("01.04.2026");
        text.ShouldContain("15.04.2026");
        text.ShouldContain("GENELTOPLAM"); // 200 + 200 = 400 + KDV 80
        text.ShouldContain("480,00");
    }

    [Test]
    public async Task Pdf_WithoutAlis_HidesPrices() {
        var env = await NewPurchasingEnv();
        var id  = (await CreatePo(env, PoBody(env, [PoLine(env.Product, 10m, 20m, new { expectedDate = "2026-04-01" })]))).GetProperty("id").GetGuid();
        await Approve(env.Client, id);
        using var reader = await OtherClient(env, "satinalma.read");

        var text = PdfTests.Text(await GetPdf(reader, id));

        text.ShouldContain("01.04.2026");
        text.ShouldNotContain("GENELTOPLAM");
        text.ShouldNotContain("Birim Fiyat".Replace(" ", ""));
        text.ShouldNotContain("240,00");
    }

    [Test]
    public async Task Email_SendsToSupplier() {
        var env = await NewPurchasingEnv();
        using var create = await env.Client.PostAsJsonAsync("/api/erp/current-accounts", new {
            code = "T9", name = "Posta Tedarikçisi", kind = "corporate", isCustomer = false, isSupplier = true, currency = "TRY", defaultDueDays = 30,
            email = "tedarikci@example.com"
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var supplier = (await create.JsonAsync()).GetProperty("id").GetGuid();
        var id       = (await CreatePo(env, PoBody(env, extra: new { supplierId = supplier }))).GetProperty("id").GetGuid();
        var subject  = "Sipariş " + Guid.NewGuid().ToString("N");

        using var draft = await env.Client.PostAsJsonAsync($"{Api}/orders/{id}/email", new { subject });
        await Approve(env.Client, id);
        using var response = await env.Client.PostAsJsonAsync($"{Api}/orders/{id}/email", new { subject, body = "Merhaba,\nSiparişimiz ektedir." });

        await Fail(draft, HttpStatusCode.Conflict, "po_not_approved");
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var message = await FindMessage(subject);
        message.ShouldNotBeNull();
        message.Value.GetProperty("To")[0].GetProperty("Address").GetString().ShouldBe("tedarikci@example.com");
        var attachment = message.Value.GetProperty("Attachments")[0];
        attachment.GetProperty("ContentType").GetString().ShouldBe("application/pdf");
        attachment.GetProperty("FileName").GetString().ShouldEndWith(".pdf");
    }

    [Test]
    public async Task Email_NoRecipient_Returns400() {
        var env = await NewPurchasingEnv(); // tedarikçi kartında e-posta yok
        var id  = (await CreatePo(env, PoBody(env))).GetProperty("id").GetGuid();
        await Approve(env.Client, id);

        using var response = await env.Client.PostAsJsonAsync($"{Api}/orders/{id}/email", new { });

        await Fail(response, HttpStatusCode.BadRequest, "no_recipient");
    }
}
