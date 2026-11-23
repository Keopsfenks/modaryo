using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modaryo.Internal;

namespace IdentityService.Web.FunctionalTests.Branches;

public class InternalBranchesTests : TestBase {
    private static string Secret => Factory.Services.GetRequiredService<IConfiguration>()["Internal:Callers:erp:Secret"]!;

    private static HttpRequestMessage Signed(string pathAndQuery, Guid? tenant, string caller = "erp", string? secret = null) {
        var ts      = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tenantS = tenant?.ToString("D");
        var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        request.Headers.Add(InternalHeaders.Service,   caller);
        request.Headers.Add(InternalHeaders.Timestamp, ts.ToString());
        request.Headers.Add(InternalHeaders.Signature,
                            InternalSignature.Compute(secret ?? Secret, "GET", pathAndQuery, ts, tenantS, null,
                                                      Encoding.UTF8.GetBytes("")));
        if (tenantS is not null) request.Headers.Add(InternalHeaders.Tenant, tenantS);
        return request;
    }

    private static async Task<Guid> CompanyWithInactiveBranchAsync() {
        using var owner    = await Factory.SignedInClientAsync("sahip@ornek.com");
        var       tenantId = await owner.CreateCompanyAsync();
        using var create = await owner.PostAsJsonAsync("/api/identity/companies/current/branches",
                                                       new { code = "S1", name = "Pasif şube" });
        var id = (await create.JsonAsync()).GetProperty("id").GetGuid();
        using var update = await owner.PutAsJsonAsync($"/api/identity/companies/current/branches/{id}",
                                                      new { code = "S1", name = "Pasif şube", isActive = false });
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        return tenantId;
    }

    [Test]
    public async Task InternalBranches_ReturnsAllBranchesOfTenant_WithoutSessionOrCsrf() {
        var       tenantId = await CompanyWithInactiveBranchAsync();
        using var other    = await Factory.SignedInClientAsync("baska@ornek.com");
        await other.CreateCompanyAsync("Diğer A.Ş.");
        using var client  = Factory.CreateClient();
        using var request = Signed($"/internal/tenants/{tenantId}/branches", tenantId);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = (await response.JsonAsync()).EnumerateArray().ToList();
        list.Select(b => b.GetProperty("code").GetString()).Order().ToArray().ShouldBe(["MRK", "S1"]);
        list.Single(b => b.GetProperty("code").GetString() == "S1").GetProperty("isActive").GetBoolean().ShouldBeFalse();
        var merkez = list.Single(b => b.GetProperty("code").GetString() == "MRK");
        merkez.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        merkez.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        merkez.GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task InternalBranches_PathTenantDiffersFromSignedTenant_Returns403() {
        var       tenantId = await CompanyWithInactiveBranchAsync();
        using var client   = Factory.CreateClient();
        using var request  = Signed($"/internal/tenants/{tenantId}/branches", Guid.CreateVersion7());

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).ShouldBe("internal_forbidden");
    }

    [Test]
    public async Task InternalBranches_UnsignedOrWrongSecretOrUnknownCaller_Returns401() {
        var       tenantId = await CompanyWithInactiveBranchAsync();
        using var client   = Factory.CreateClient();
        var       path     = $"/internal/tenants/{tenantId}/branches";

        using var unsigned    = await client.GetAsync(path);
        using var wrongSecret = await client.SendAsync(Signed(path, tenantId, secret: "yanlis-sir-0123456789"));
        using var unknown     = await client.SendAsync(Signed(path, tenantId, "bilinmeyen"));

        unsigned.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrongSecret.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task InternalWrite_IdentityHasNoIdempotencyStore_Returns405() {
        var       tenantId = Guid.CreateVersion7();
        var       path     = $"/internal/tenants/{tenantId}/branches";
        var       ts       = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var       body     = Encoding.UTF8.GetBytes("{}");
        using var client   = Factory.CreateClient();
        using var request  = new HttpRequestMessage(HttpMethod.Post, path) {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add(InternalHeaders.Service,        "erp");
        request.Headers.Add(InternalHeaders.Timestamp,      ts.ToString());
        request.Headers.Add(InternalHeaders.Tenant,         tenantId.ToString("D"));
        request.Headers.Add(InternalHeaders.IdempotencyKey, "k-1");
        request.Headers.Add(InternalHeaders.Signature,
                            InternalSignature.Compute(Secret, "POST", path, ts, tenantId.ToString("D"), "k-1", body));

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await response.ErrorCodeAsync()).ShouldBe("internal_method_not_allowed");
    }
}
