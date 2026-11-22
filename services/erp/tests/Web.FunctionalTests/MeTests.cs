using System.Text.Json.Nodes;

namespace ERP.Web.FunctionalTests;

public class MeTests : TestBase {
    [Test]
    public async Task Me_WithValidSession_ReturnsIdentityFromRecord() {
        var record = ContractSessions.Example();
        record["permissions"] = new JsonArray("members.read", "company.update");
        var       sid    = await ContractSessions.StoreAsync(record);
        using var client = Factory.CreateClientWithSession(sid);

        using var response = await client.GetAsync("/api/erp/me");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("userId").GetGuid().ShouldBe(Guid.Parse("0193a6f2-6c1e-7b3a-9d4e-2f5a8c7b1e90"));
        body.GetProperty("email").GetString().ShouldBe("ali@ornek.com");
        body.GetProperty("fullName").GetString().ShouldBe("Ali Veli");
        body.GetProperty("tenantId").GetGuid().ShouldBe(Guid.Parse("0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b21"));
        body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .ShouldBe(["members.read", "company.update"]);
    }

    [Test]
    public async Task Me_WithoutCookie_Returns401() {
        using var client = Factory.CreateClientWithSession(null);

        (await client.GetAsync("/api/erp/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Me_AfterKeyDeleted_Returns401() {
        var       sid    = await ContractSessions.StoreAsync(ContractSessions.Example());
        using var client = Factory.CreateClientWithSession(sid);
        (await client.GetAsync("/api/erp/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ContractSessions.DeleteAsync(sid);

        (await client.GetAsync("/api/erp/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Me_WithUnknownContractVersion_Returns401() {
        var record = ContractSessions.Example();
        record["v"] = 2;
        var       sid    = await ContractSessions.StoreAsync(record);
        using var client = Factory.CreateClientWithSession(sid);

        (await client.GetAsync("/api/erp/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Post_WithoutCsrfHeader_Returns403() {
        var       sid     = await ContractSessions.StoreAsync(ContractSessions.Example());
        using var client  = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/erp/me");
        request.Headers.Add("Cookie", $"modaryo_sid={sid}");

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).ShouldBe("csrf_header_missing");
    }
}