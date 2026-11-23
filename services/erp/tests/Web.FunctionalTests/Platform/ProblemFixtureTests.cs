using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ERP.Web.FunctionalTests.Platform;

/// <summary>
///     Hata gövdesinin (ProblemDetails) gerçek ERP yanıtlarından alınmış örneklerle (contracts/fixtures/problem-*.json)
///     birebir aynı kaldığını doğrular. Go servisleri aynı dosyalarla karşılaştırılır.
///     Dosyaları yeniden üretmek için MODARYO_WRITE_FIXTURES=1 ile çalıştırın.
/// </summary>
public class ProblemFixtureTests : TestBase {
    private static readonly Guid Tenant  = Guid.Parse("0197a3c2-5b1e-7c3a-9d2f-4e6b8a1c0d11");
    private static readonly Guid Missing = Guid.Parse("0197a3c2-5b1e-7c3a-9d2f-4e6b8a1c0d99");

    private static readonly JsonSerializerOptions Pretty = new() {
                                                                     WriteIndented = true,
                                                                     Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                                                                 };

    private static string FixturePath(string name) {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "contracts", "fixtures")))
            dir = dir.Parent;

        if (dir is null) throw new DirectoryNotFoundException("contracts/fixtures bulunamadı.");

        return Path.Combine(dir.FullName, "contracts", "fixtures", name);
    }

    private static async Task AssertFixtureAsync(HttpResponseMessage response,
                                                 HttpStatusCode      status,
                                                 string              fixture
    ) {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType.ShouldNotBeNull();
        response.Content.Headers.ContentType.MediaType.ShouldBe("application/problem+json");

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");

        var path = FixturePath(fixture);
        if (Environment.GetEnvironmentVariable("MODARYO_WRITE_FIXTURES") == "1") {
            await File.WriteAllTextAsync(path, body.ToJsonString(Pretty) + Environment.NewLine,
                                         new System.Text.UTF8Encoding(false));
            return;
        }

        var expected = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        JsonNode.DeepEquals(body, expected).ShouldBeTrue($"{fixture} farklı.\nGerçek: {body.ToJsonString(Pretty)}");
    }

    [Test]
    public async Task Validation_MatchesFixture() {
        using var client = await Factory.ClientAsync(Tenant, "cari.read", "cari.manage");

        using var response = await client.PostAsJsonAsync("/api/erp/current-accounts",
                                                          new { code = "", name = "", kind = "corporate", currency = "TRY" });

        await AssertFixtureAsync(response, HttpStatusCode.BadRequest, "problem-validation.json");
    }

    [Test]
    public async Task Unauthorized_MatchesFixture() {
        using var client = Factory.CreateClientWithSession(null);

        using var response = await client.GetAsync("/api/erp/current-accounts");

        await AssertFixtureAsync(response, HttpStatusCode.Unauthorized, "problem-unauthorized.json");
    }

    [Test]
    public async Task Csrf_MatchesFixture() {
        using var client = await Factory.ClientAsync(Tenant, "cari.read", "cari.manage");
        client.DefaultRequestHeaders.Remove("X-Modaryo-Csrf");

        using var response = await client.PostAsJsonAsync("/api/erp/current-accounts", new { code = "C0001" });

        await AssertFixtureAsync(response, HttpStatusCode.Forbidden, "problem-csrf.json");
    }

    [Test]
    public async Task Forbidden_MatchesFixture() {
        using var client = await Factory.ClientAsync(Tenant, "cari.read");

        using var response = await client.PostAsJsonAsync("/api/erp/current-accounts", new { code = "C0001" });

        await AssertFixtureAsync(response, HttpStatusCode.Forbidden, "problem-forbidden.json");
    }

    [Test]
    public async Task NotFound_MatchesFixture() {
        using var client = await Factory.ClientAsync(Tenant, "cari.read");

        using var response = await client.GetAsync($"/api/erp/current-accounts/{Missing}");

        await AssertFixtureAsync(response, HttpStatusCode.NotFound, "problem-not-found.json");
    }

    [Test]
    public async Task AppException_MatchesFixture() {
        using var client = await Factory.ClientAsync(Tenant, modules: ["sube"]);

        using var response = await client.GetAsync("/test-probe/module");

        await AssertFixtureAsync(response, HttpStatusCode.Forbidden, "problem-app.json");
    }

    [Test]
    public async Task InternalUnauthorized_MatchesFixture() {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("/internal/ping");

        await AssertFixtureAsync(response, HttpStatusCode.Unauthorized, "problem-internal-unauthorized.json");
    }
}
