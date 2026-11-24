using ERP.Application.Common.Interfaces;

namespace ERP.Web.FunctionalTests.Invoicing;

public class SeriesTests : TestBase {
    private const           string Url     = "/api/erp/document-series";
    private static readonly Guid   Tenant  = Guid.CreateVersion7();
    private static readonly Guid   BranchA = Guid.CreateVersion7();
    private static readonly Guid   BranchB = Guid.CreateVersion7();

    private static Task<HttpClient> Admin(Guid[]? branches = null) {
        TestEnvironment.Factory.Branches.Seed(Tenant, [new BranchRef(BranchA, "A", "A Şube", true, true), new BranchRef(BranchB, "B", "B Şube", true, false)]);
        return Factory.ClientAsync(Tenant, null, branches, null, ["fatura.read", "ayarlar.belgeler"]);
    }

    private static Task<HttpResponseMessage> Create(HttpClient client, string prefix, string mode = "Paper", string group = "sales", Guid? branch = null,
                                                    bool isDefault = false, int? year = null) {
        return client.PostAsJsonAsync(Url, new { branchId = branch ?? BranchA, kindGroup = group, mode, prefix, year, isDefault });
    }

    [Test]
    public async Task Series_EBelgePrefixMustBe3() {
        using var client = await Admin();

        foreach (var bad in new[] { "AB", "ABCD", "ab1", "A-C", "A B", "ÇAB" }) {
            using var response = await Create(client, bad, "EInvoice");
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, bad);
        }

        using var ok = await Create(client, "ABC", "EInvoice", year: 2026);
        ok.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ok.JsonAsync();
        body.GetProperty("prefix").GetString().ShouldBe("ABC");
        body.GetProperty("nextPreview").GetString().ShouldBe("ABC2026000000001");
        body.GetProperty("nextPreview").GetString()!.Length.ShouldBe(16);

        using var digits = await Create(client, "X9Z", "EArchive");
        digits.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Series_OneDefaultPerGroupBranchMode() {
        using var client = await Admin();

        var first  = await (await Create(client, "AAA", "EInvoice")).JsonAsync();
        var second = await (await Create(client, "BBB", "EInvoice", isDefault: true)).JsonAsync();
        var other  = await (await Create(client, "CCC", "EInvoice", group: "purchase")).JsonAsync();
        var branch = await (await Create(client, "DDD", "EInvoice", branch: BranchB)).JsonAsync();

        // İlk seri otomatik varsayılan; ikincisi isDefault ile devralır.
        first.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        second.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        var list = await (await client.GetAsync(Url)).JsonAsync();
        DefaultOf(list, "AAA").ShouldBeFalse();
        DefaultOf(list, "BBB").ShouldBeTrue();
        // Farklı grup ve şubedeki varsayılanlar etkilenmez.
        DefaultOf(list, "CCC").ShouldBeTrue();
        DefaultOf(list, "DDD").ShouldBeTrue();

        // PUT ile eskisini yeniden varsayılan yap.
        using var put = await client.PutAsJsonAsync($"{Url}/{first.GetProperty("id").GetGuid()}",
                                                    new { kindGroup = "sales", mode = "EInvoice", prefix = "AAA", isDefault = true, isActive = true });
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        list = await (await client.GetAsync(Url)).JsonAsync();
        DefaultOf(list, "AAA").ShouldBeTrue();
        DefaultOf(list, "BBB").ShouldBeFalse();
        list.EnumerateArray().Count(s => s.GetProperty("isDefault").GetBoolean() && s.GetProperty("branchId").GetGuid() == BranchA
                                         && s.GetProperty("kindGroup").GetString() == "sales").ShouldBe(1);
        _ = other;
        _ = branch;
    }

    [Test]
    public async Task Series_DuplicatePrefixSameYear_Returns409() {
        using var client = await Admin();
        (await Create(client, "AAA", "EInvoice", year: 2026)).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var duplicate = await Create(client, "AAA", "EInvoice", branch: BranchB, year: 2026);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ErrorCodeAsync()).ShouldBe("prefix_taken");
        using var sameIdentity = await Create(client, "AAA", "EInvoice", year: 2026);
        (await sameIdentity.ErrorCodeAsync()).ShouldBe("series_exists");
        using var nextYear = await Create(client, "AAA", "EInvoice", year: 2027);
        nextYear.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Series_RequiresPermissionsAndBranchAccess() {
        using var reader = await Factory.ClientAsync(Tenant, null, null, null, ["fatura.read"]);
        using var denied = await Create(reader, "AAA");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var onlyB = await Admin([BranchB]);
        using var forbidden = await Create(onlyB, "AAA", branch: BranchA);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await forbidden.ErrorCodeAsync()).ShouldBe("branch_forbidden");
    }

    private static bool DefaultOf(System.Text.Json.JsonElement list, string prefix) {
        return list.EnumerateArray().First(s => s.GetProperty("prefix").GetString() == prefix).GetProperty("isDefault").GetBoolean();
    }
}
