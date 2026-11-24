using System.Text.Json;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;

namespace ERP.Web.FunctionalTests.Invoicing;

public class EmailTests : TestBase {
    private const string Mailpit = "http://localhost:8025/api/v1";

    private static readonly HttpClient Http = new();

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

    private static async Task SetFirmName(Env env, string legalName) {
        using var client   = await OtherClient(env, "ayarlar.firma");
        using var response = await client.PutAsJsonAsync("/api/erp/settings/firma", new { version = 0, value = new { legalName } });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Email_SendsWithPdfAttachmentToMailpit() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        var issued  = await CreateAndIssue(env, Body(env));
        var id      = issued.GetProperty("id").GetGuid();
        var subject = "Fatura " + Guid.NewGuid().ToString("N");

        using var response = await env.Client.PostAsJsonAsync($"{Url}/{id}/email", new {
            to = new[] { "alici@example.com" }, cc = new[] { "muhasebe@example.com" }, subject, body = "Merhaba,\nFaturanız ektedir. <b>İyi çalışmalar</b>"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var message = await FindMessage(subject);
        message.ShouldNotBeNull();
        message.Value.GetProperty("To")[0].GetProperty("Address").GetString().ShouldBe("alici@example.com");
        message.Value.GetProperty("Cc")[0].GetProperty("Address").GetString().ShouldBe("muhasebe@example.com");
        message.Value.GetProperty("From").GetProperty("Address").GetString().ShouldBe("belge@modaryo.local");
        var attachment = message.Value.GetProperty("Attachments")[0];
        attachment.GetProperty("FileName").GetString().ShouldBe("FTR2026000000001.pdf");
        attachment.GetProperty("ContentType").GetString().ShouldBe("application/pdf");
        // Gövde düz metin olarak ele alınır: HTML etiketleri kaçırılır.
        message.Value.GetProperty("HTML").GetString()!.ShouldContain("&lt;b&gt;");

        var bytes = await Http.GetByteArrayAsync($"{Mailpit}/message/{message.Value.GetProperty("ID").GetString()}/part/{attachment.GetProperty("PartID").GetString()}");
        bytes.AsSpan(0, 5).ToArray().ShouldBe("%PDF-"u8.ToArray());
        PdfTests.Text(bytes).ShouldContain("FTR2026000000001");
    }

    [Test]
    public async Task Email_TemplateVariablesReplaced() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        var firm = "Şablon " + Guid.NewGuid().ToString("N")[..10] + " A.Ş.";
        await SetFirmName(env, firm);
        using (var settings = await OtherClient(env, "ayarlar.belgeler")) {
            using var put = await settings.PutAsJsonAsync("/api/erp/settings/belgeler", new {
                version = 0,
                value = new {
                    accentColor      = "#112233", showIbans = true, invoiceEmailSubject = "{firma} - {belgeNo} numaralı fatura",
                    invoiceEmailBody = "Sayın {alici}, {belgeNo} numaralı {tutar} tutarlı faturanız {vade} vadelidir. {firma}"
                }
            });
            put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        }

        var issued = await CreateAndIssue(env, Body(env)); // 340,00 TL, vade 30 gün sonra

        using var response = await env.Client.PostAsJsonAsync($"{Url}/{issued.GetProperty("id").GetGuid()}/email", new { to = new[] { "alici@example.com" } });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var message = await FindMessage($"{firm} - FTR2026000000001 numaralı fatura");
        message.ShouldNotBeNull();
        var html = message.Value.GetProperty("HTML").GetString()!;
        html.ShouldContain("Sayın Cari M1, FTR2026000000001 numaralı 340,00 TL tutarlı faturanız 09.04.2026 vadelidir.");
        html.ShouldContain(System.Net.WebUtility.HtmlEncode(firm));
        html.ShouldNotContain("{");
    }

    [Test]
    public async Task Email_DraftAndCancelled_Return409() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        var draft  = await Create(env, Body(env));
        var issued = await CreateAndIssue(env, Body(env));
        (await PostingSupport.Cancel(env.Client, issued.GetProperty("id").GetGuid())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var to = new { to = new[] { "alici@example.com" } };

        using var fromDraft     = await env.Client.PostAsJsonAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/email", to);
        using var fromCancelled = await env.Client.PostAsJsonAsync($"{Url}/{issued.GetProperty("id").GetGuid()}/email", to);

        await Fail(fromDraft, HttpStatusCode.Conflict, "invoice_not_issued");
        await Fail(fromCancelled, HttpStatusCode.Conflict, "invoice_cancelled");
    }

    [Test]
    public async Task Email_ValidatesRecipients_AndPermission() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        var issued = await CreateAndIssue(env, Body(env));
        var url    = $"{Url}/{issued.GetProperty("id").GetGuid()}/email";
        using var limited = await OtherClient(env, "cari.read");

        using var none    = await env.Client.PostAsJsonAsync(url, new { to = Array.Empty<string>() });
        using var invalid = await env.Client.PostAsJsonAsync(url, new { to = new[] { "geçersiz" } });
        using var badCc   = await env.Client.PostAsJsonAsync(url, new { to = new[] { "a@example.com" }, cc = new[] { "x\r\nBcc: z@example.com" } });
        using var noPerm  = await limited.PostAsJsonAsync(url, new { to = new[] { "a@example.com" } });

        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        badCc.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noPerm.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
