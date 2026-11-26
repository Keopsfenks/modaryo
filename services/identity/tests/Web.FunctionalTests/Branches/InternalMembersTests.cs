using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modaryo.Internal;

namespace IdentityService.Web.FunctionalTests.Branches;

public class InternalMembersTests : TestBase {
    private static string Secret => Factory.Services.GetRequiredService<IConfiguration>()["Internal:Callers:erp:Secret"]!;

    private static HttpRequestMessage Signed(string pathAndQuery, Guid tenant) {
        var ts      = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tenantS = tenant.ToString("D");
        var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        request.Headers.Add(InternalHeaders.Service,   "erp");
        request.Headers.Add(InternalHeaders.Timestamp, ts.ToString());
        request.Headers.Add(InternalHeaders.Signature,
                            InternalSignature.Compute(Secret, "GET", pathAndQuery, ts, tenantS, null, Encoding.UTF8.GetBytes("")));
        request.Headers.Add(InternalHeaders.Tenant, tenantS);
        return request;
    }

    [Test]
    public async Task InternalMember_ReturnsMember_And404ForNonMember_And403ForOtherTenant() {
        using var owner    = await Factory.SignedInClientAsync("sahip@ornek.com");
        var       tenantId = await owner.CreateCompanyAsync();
        using var list     = await owner.GetAsync("/api/identity/companies/current/members");
        var       userId   = (await list.JsonAsync()).EnumerateArray().First().GetProperty("userId").GetGuid();
        using var client   = Factory.CreateClient();

        using var found    = await client.SendAsync(Signed($"/internal/tenants/{tenantId}/members/{userId}", tenantId));
        using var missing  = await client.SendAsync(Signed($"/internal/tenants/{tenantId}/members/{Guid.CreateVersion7()}", tenantId));
        using var foreign  = await client.SendAsync(Signed($"/internal/tenants/{tenantId}/members/{userId}", Guid.CreateVersion7()));

        found.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await found.JsonAsync();
        body.GetProperty("userId").GetGuid().ShouldBe(userId);
        body.GetProperty("fullName").GetString().ShouldNotBeNullOrEmpty();
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
