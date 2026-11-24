using System.Net.Http.Headers;
using UglyToad.PdfPig;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;

namespace ERP.Web.FunctionalTests.Invoicing;

public class PdfTests : TestBase {
    private const string Iban = "TR330006100519786457841326";

    /// <summary>1x1 PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    internal static async Task<byte[]> GetPdf(HttpClient client, Guid id) {
        using var response = await client.GetAsync($"{Url}/{id}/pdf");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>Tüm sayfaların harfleri, boşluksuz (PdfPig kelime ayırması yazı tipine göre değişir).</summary>
    internal static string Text(byte[] pdf) {
        using var document = PdfDocument.Open(pdf);
        return string.Concat(document.GetPages().SelectMany(p => p.Letters).Select(l => l.Value)).Replace(" ", "").Replace(" ", "");
    }

    private static async Task SetFirm(Env env, string legalName, bool withLogo) {
        using var client = await OtherClient(env, "ayarlar.firma", "ayarlar.belgeler");
        Guid? logo = null;
        if (withLogo) {
            var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(Png);
            part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(part, "file", "logo.png");
            using var upload = await client.PostAsync("/api/erp/files?kind=logo", form);
            upload.StatusCode.ShouldBe(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
            logo = (await upload.JsonAsync()).GetProperty("id").GetGuid();
        }

        using var response = await client.PutAsJsonAsync("/api/erp/settings/firma", new {
            version = 0,
            value = new {
                legalName, taxNumber = "1234567890", taxOffice = "Kadıköy", address = "Bağdat Cad. No 5", city = "İstanbul", district = "Kadıköy", logoFileId = logo,
                ibans = new[] { new { bankName = "Örnek Bankası", iban = Iban, currency = "TRY" } }
            }
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Pdf_ContainsNumberTotalsAmountInWordsAndIbans() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        await SetFirm(env, "Örnek Tekstil A.Ş.", withLogo: true);
        var issued = await CreateAndIssue(env, Body(env)); // 2 x 50 (KDV %20) + 200 hizmet (KDV %10) = 300 + 40 KDV

        var pdf  = await GetPdf(env.Client, issued.GetProperty("id").GetGuid());
        var text = Text(pdf);

        pdf.AsSpan(0, 5).ToArray().ShouldBe("%PDF-"u8.ToArray());
        text.ShouldContain("F000001");
        text.ShouldContain("340,00");
        text.ShouldContain("YalnızÜçYüzKırkTürkLirası");
        text.ShouldContain(Iban);
        text.ShouldContain("ÖrnekTekstilA.Ş.");
        text.ShouldContain("CariM1");
        text.ShouldNotContain("TASLAK");
    }

    [Test]
    public async Task Pdf_DraftHasTaslakWatermark() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var draft  = await Create(env, Body(env));
        var issued = await CreateAndIssue(env, Body(env));

        var draftText  = Text(await GetPdf(env.Client, draft.GetProperty("id").GetGuid()));
        var issuedText = Text(await GetPdf(env.Client, issued.GetProperty("id").GetGuid()));

        draftText.ShouldContain("TASLAK");
        draftText.ShouldNotContain("F00000");
        draftText.ShouldNotContain("FaturaNo");
        issuedText.ShouldNotContain("TASLAK");
    }

    [Test]
    public async Task Pdf_CancelledHasIptalWatermark_AndKeepsNumber() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var issued = await CreateAndIssue(env, Body(env));
        var id     = issued.GetProperty("id").GetGuid();
        (await PostingSupport.Cancel(env.Client, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var text = Text(await GetPdf(env.Client, id));

        text.ShouldContain("İPTAL");
        text.ShouldContain("F000001");
    }

    [Test]
    public async Task Pdf_TurkishCharactersRender() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var cari   = await CreateCari(env.Client, "M9", customer: true, supplier: false, name: "İŞÇĞÜÖı ışığı şğüöçi Ltd");
        var issued = await CreateAndIssue(env, Body(env, extra: new { currentAccountId = cari }));

        var pdf  = await GetPdf(env.Client, issued.GetProperty("id").GetGuid());
        var text = Text(pdf);

        text.ShouldContain("İŞÇĞÜÖı");
        text.ShouldContain("ışığışğüöçiLtd");
        // Her harfin gerçek bir glifi olmalı (eksik glif sıfır boyutlu çıkar).
        using var document = PdfDocument.Open(pdf);
        var letters = document.GetPages().SelectMany(p => p.Letters).Where(l => l.Value.Length == 1 && "İŞÇĞÜÖışğüöç".Contains(l.Value)).ToList();
        letters.Count.ShouldBeGreaterThanOrEqualTo(20);
        letters.ShouldAllBe(l => l.BoundingBox.Width > 0 && l.BoundingBox.Height > 0);
    }

    [Test]
    public async Task Pdf_PurchaseRequiresFiyatAlis_AndUnknownReturns404() {
        var env = await NewEnv();
        await CreateSeries(env, "A", group: "purchase");
        var issued = await CreateAndIssue(env, Body(env, "Purchase"));
        using var limited = await OtherClient(env, "fatura.read");

        using var forbidden = await limited.GetAsync($"{Url}/{issued.GetProperty("id").GetGuid()}/pdf");
        using var missing   = await env.Client.GetAsync($"{Url}/{Guid.CreateVersion7()}/pdf");

        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Pdf_ForeignCurrencyShowsCodeAndWords() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var issued = await CreateAndIssue(env, Body(env, lines: [FreeLine("Danışmanlık", 1m, 100m, 0)], extra: new { currency = "USD", rate = 30m }));

        var text = Text(await GetPdf(env.Client, issued.GetProperty("id").GetGuid()));

        text.ShouldContain("100,00");
        text.ShouldContain("YalnızYüzAmerikanDoları");
        text.ShouldContain("USD");
    }

    [Test]
    public async Task SamplePdf_UsesFirmAndDocumentSettings_AndNeedsAyarlarBelgeler() {
        var env = await NewEnv();
        await SetFirm(env, "Önizleme Ltd.", withLogo: false);

        using var response = await env.Client.GetAsync($"{Url}/sample-pdf");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var text = Text(await response.Content.ReadAsByteArrayAsync());

        text.ShouldContain("ÖnizlemeLtd.");
        text.ShouldContain("ÖrnekMüşteri");
        text.ShouldContain(Iban);
        text.ShouldNotContain("TASLAK");

        using var denied = await (await OtherClient(env, "fatura.read")).GetAsync($"{Url}/sample-pdf");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
