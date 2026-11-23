using System.Net.Http.Headers;

namespace ERP.Web.FunctionalTests.Files;

public class FileTests : TestBase {
    private const           string Files   = "/api/erp/files";
    private const           string Firma   = "/api/erp/settings/firma";
    private static readonly Guid   TenantA = Guid.CreateVersion7();
    private static readonly Guid   TenantB = Guid.CreateVersion7();

    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Pdf       = [.. "%PDF-1.7\n%%EOF"u8];

    private static byte[] Png(int size = 64) {
        var bytes = new byte[size];
        new Random(7).NextBytes(bytes);
        PngHeader.CopyTo(bytes, 0);
        return bytes;
    }

    private static Task<HttpClient> Member(Guid tenant, params string[] permissions) {
        return Factory.ClientAsync(tenant, permissions: permissions.Length == 0 ? ["cari.read"] : permissions);
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client,
                                                    string?    kind,
                                                    byte[]     content,
                                                    string     fileName    = "dosya.png",
                                                    string     contentType = "image/png"
    ) {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(part, "file", fileName);
        return client.PostAsync(kind is null ? Files : $"{Files}?kind={kind}", form);
    }

    private static async Task<Guid> UploadedId(HttpClient client, string kind) {
        using var response = await Upload(client, kind, Png());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    [Test]
    public async Task UploadPng_Returns201AndDownloadsSameBytes() {
        using var client = await Member(TenantA);
        var       bytes  = Png(2048);

        using var created = await Upload(client, "image", bytes, "Şirket logosu.png");

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await created.JsonAsync();
        body.GetProperty("contentType").GetString().ShouldBe("image/png");
        body.GetProperty("size").GetInt64().ShouldBe(bytes.Length);
        body.GetProperty("fileName").GetString().ShouldBe("Şirket logosu.png");

        using var download = await client.GetAsync($"{Files}/{body.GetProperty("id").GetGuid()}");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        download.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        download.Headers.GetValues("Content-Security-Policy").ShouldContain("sandbox; default-src 'none'");
        download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("inline");
        download.Content.Headers.ContentDisposition.FileNameStar.ShouldBe("Şirket logosu.png");
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);
    }

    [Test]
    public async Task Upload_ContentTypeComesFromMagicBytes_NotHeaderOrName() {
        using var client = await Member(TenantA);

        using var response = await Upload(client, "attachment", Pdf, "fatura.png");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.JsonAsync()).GetProperty("contentType").GetString().ShouldBe("application/pdf");
    }

    [Test]
    public async Task UploadOver10Mb_Returns413() {
        using var client = await Member(TenantA);

        using var response = await Upload(client, "attachment", Png(10 * 1024 * 1024 + 1));

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await response.ErrorCodeAsync()).ShouldBe("file_too_large");
    }

    [Test]
    public async Task Upload10Mb_Exactly_Returns201() {
        using var client = await Member(TenantA);

        using var response = await Upload(client, "attachment", Png(10 * 1024 * 1024));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task UploadLogoOver2Mb_Returns413() {
        using var client = await Member(TenantA, "ayarlar.firma");

        using var response = await Upload(client, "logo", Png(2 * 1024 * 1024 + 1));

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await response.ErrorCodeAsync()).ShouldBe("file_too_large");
    }

    [Test]
    public async Task Upload_ExecutableRenamedPng_Returns415() {
        using var client = await Member(TenantA);
        byte[] exe = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF];

        using var response = await Upload(client, "image", exe, "zararli.png");

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await response.ErrorCodeAsync()).ShouldBe("file_type_not_allowed");
    }

    [Test]
    public async Task Upload_PdfAsImageOrLogo_Returns415() {
        using var client = await Member(TenantA, "ayarlar.firma");

        (await Upload(client, "image", Pdf)).StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await Upload(client, "logo", Pdf)).StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Test]
    public async Task Upload_UnknownOrMissingKind_Returns400() {
        using var client = await Member(TenantA);

        (await Upload(client, "belge", Png())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Upload(client, null, Png())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Upload_Logo_RequiresCompanyPermission() {
        using var member = await Member(TenantA);
        using var admin  = await Member(TenantA, "ayarlar.firma");

        (await Upload(member, "logo", Png())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Upload(admin, "logo", Png())).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Upload_WithoutFilePart_Returns400() {
        using var client = await Member(TenantA);

        using var response = await client.PostAsync($"{Files}?kind=image", new MultipartFormDataContent());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Upload_WithoutSession_Returns401() {
        using var client = Factory.CreateClientWithSession(null);

        using var response = await Upload(client, "image", Png());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Download_OtherTenantFile_Returns404() {
        using var owner = await Member(TenantA);
        using var other = await Member(TenantB);
        var       id    = await UploadedId(owner, "image");

        (await owner.GetAsync($"{Files}/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await other.GetAsync($"{Files}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"{Files}/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task CompanyProfile_LogoFromOtherTenant_Returns400() {
        using var owner = await Member(TenantA, "ayarlar.firma");
        using var other = await Member(TenantB, "ayarlar.firma");
        var       id    = await UploadedId(owner, "logo");

        using var response = await other.PutAsJsonAsync(Firma, new { version = 0, value = new { logoFileId = id } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CompanyProfile_LogoMissingOrNotLogoKind_Returns400() {
        using var client = await Member(TenantA, "ayarlar.firma");
        var       image  = await UploadedId(client, "image");

        (await client.PutAsJsonAsync(Firma, new { version = 0, value = new { logoFileId = Guid.NewGuid() } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(Firma, new { version = 0, value = new { logoFileId = image } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CompanyProfile_OwnLogo_Saves() {
        using var client = await Member(TenantA, "ayarlar.firma");
        var       id     = await UploadedId(client, "logo");

        using var response = await client.PutAsJsonAsync(Firma, new { version = 0, value = new { logoFileId = id } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await client.GetAsync(Firma)).JsonAsync()).GetProperty("value").GetProperty("logoFileId").GetGuid()
            .ShouldBe(id);
    }
}
