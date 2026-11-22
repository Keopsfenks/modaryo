using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Modaryo.Sessions.Tests;

/// <summary>Oturum sözleşmesi v1'in sonradan eklenen alanları: kullanıcı türü, modüller, şubeler, iskonto sınırı, cari.</summary>
public class SessionContractV1Tests
{
    private static readonly Guid BranchA = Guid.Parse("0193a6f5-2c64-7e33-8a4d-8fb16c5e7d43");
    private static readonly Guid BranchB = Guid.Parse("0193a6f5-2c64-7e33-8a4d-8fb16c5e7d44");

    private WebApplication _defaultApp = null!;
    private HttpClient _defaultClient = null!;
    private WebApplication _openApp = null!;
    private HttpClient _openClient = null!;

    [OneTimeSetUp]
    public async Task StartHosts()
    {
        (_defaultApp, _defaultClient) = await StartHostAsync(configure: null);
        (_openApp, _openClient) = await StartHostAsync(o =>
        {
            o.AllowedUserTypes.Add(SessionUserTypes.Dealer);
            o.AllowedUserTypes.Add(SessionUserTypes.SelfService);
        });
    }

    [OneTimeTearDown]
    public async Task StopHosts()
    {
        _defaultClient.Dispose();
        _openClient.Dispose();
        await _defaultApp.DisposeAsync();
        await _openApp.DisposeAsync();
    }

    private static async Task<(WebApplication, HttpClient)> StartHostAsync(Action<SessionAuthenticationOptions>? configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddModaryoSessions(ValkeyFixture.ConnectionString, configure);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/claims", (ClaimsPrincipal user) => Results.Ok(new
        {
            userType = user.GetUserType(),
            hasCari = user.HasModule("cari"),
            hasBanka = user.HasModule("banka"),
            branchIds = user.GetBranchIds(),
            canA = user.CanAccessBranch(BranchA),
            canB = user.CanAccessBranch(BranchB),
            canOther = user.CanAccessBranch(Guid.CreateVersion7()),
            maxDiscount = user.GetMaxDiscountPercent(),
            cariId = user.GetCariId(),
        })).RequireAuthorization();

        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "contracts")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("contracts klasörünü içeren depo kökü bulunamadı.");
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoRoot(), "contracts", "fixtures", name));

    private static SessionRecord NewRecord() => ValkeyFixture.NewRecord(DateTimeOffset.UtcNow, TimeSpan.FromHours(24));

    private static async Task<JsonElement> GetClaimsAsync(HttpClient client, SessionRecord record, int expectedStatus = 200)
    {
        var sid = await ValkeyFixture.StoreAsync(JsonSerializer.Serialize(record, SessionRecord.Json), TimeSpan.FromHours(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/claims");
        request.Headers.Add("Cookie", $"{SessionKeys.CookieName}={sid}");
        using var response = await client.SendAsync(request);
        ((int)response.StatusCode).ShouldBe(expectedStatus);
        return expectedStatus == 200
            ? JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone()
            : default;
    }

    [Test]
    public async Task Reader_RecordWithoutNewFields_DefaultsToEmployeeAllBranches()
    {
        var node = JsonNode.Parse(Fixture("session-v1-legacy.json"))!.AsObject();
        foreach (var field in new[] { "user_type", "cari_id", "modules", "branch_ids", "max_discount_pct" })
        {
            node.ContainsKey(field).ShouldBeFalse();
        }

        var now = DateTimeOffset.UtcNow;
        node["created_at"] = now.ToUnixTimeSeconds();
        node["absolute_expires_at"] = now.AddHours(24).ToUnixTimeSeconds();
        var sid = await ValkeyFixture.StoreAsync(node.ToJsonString(), TimeSpan.FromHours(8));

        var lookup = await new SessionReader(ValkeyFixture.Redis, TimeProvider.System).ReadAsync(sid, CancellationToken.None);

        lookup.ShouldNotBeNull();
        lookup.Record.UserType.ShouldBe("employee");
        lookup.Record.Modules.ShouldBeEmpty();
        lookup.Record.BranchIds.ShouldBeNull();
        lookup.Record.MaxDiscountPct.ShouldBe(0m);
        lookup.Record.CariId.ShouldBeNull();
    }

    [Test]
    public void Record_RoundTripsNewFieldsInSnakeCase()
    {
        var record = NewRecord() with
        {
            UserType = SessionUserTypes.Dealer,
            CariId = Guid.CreateVersion7(),
            Modules = ["cari"],
            BranchIds = [BranchA],
            MaxDiscountPct = 7.5m,
        };

        var json = JsonSerializer.Serialize(record, SessionRecord.Json);

        foreach (var name in new[] { "\"user_type\"", "\"branch_ids\"", "\"max_discount_pct\"", "\"cari_id\"", "\"modules\"" })
        {
            json.ShouldContain(name);
        }

        var back = JsonSerializer.Deserialize<SessionRecord>(json, SessionRecord.Json)!;
        back.UserType.ShouldBe("dealer");
        back.CariId.ShouldBe(record.CariId);
        back.Modules.ShouldBe(["cari"]);
        back.BranchIds.ShouldBe([BranchA]);
        back.MaxDiscountPct.ShouldBe(7.5m);
    }

    [Test]
    public void FullFixture_Deserializes()
    {
        var record = JsonSerializer.Deserialize<SessionRecord>(Fixture("session-v1-full.json"), SessionRecord.Json)!;

        record.UserType.ShouldBe("dealer");
        record.CariId.ShouldBe(Guid.Parse("0193a6f4-1b53-7d22-9f3c-7ea05b4d6c32"));
        record.Modules.ShouldBe(["pos", "sube"]);
        record.BranchIds.ShouldBe([BranchA, BranchB]);
        record.MaxDiscountPct.ShouldBe(7.5m);
    }

    [Test]
    public async Task Handler_DealerSession_RejectedByDefault()
    {
        await GetClaimsAsync(_defaultClient, NewRecord() with { UserType = SessionUserTypes.Dealer }, expectedStatus: 401);
    }

    [Test]
    public async Task Handler_UnknownUserType_Rejected()
    {
        await GetClaimsAsync(_openClient, NewRecord() with { UserType = "alien" }, expectedStatus: 401);
    }

    [Test]
    public async Task Handler_EmployeeSession_AcceptedByDefault()
    {
        var claims = await GetClaimsAsync(_defaultClient, NewRecord());

        claims.GetProperty("userType").GetString().ShouldBe("employee");
    }

    [Test]
    public async Task Handler_DealerSession_AcceptedWhenAllowed()
    {
        var claims = await GetClaimsAsync(_openClient, NewRecord() with { UserType = SessionUserTypes.Dealer });

        claims.GetProperty("userType").GetString().ShouldBe("dealer");
    }

    [Test]
    public async Task Claims_ExposeModulesBranchesDiscountCari()
    {
        var cariId = Guid.CreateVersion7();
        var record = NewRecord() with
        {
            UserType = SessionUserTypes.Dealer,
            CariId = cariId,
            Modules = ["cari", "satis"],
            BranchIds = [BranchA],
            MaxDiscountPct = 12.5m,
        };

        var claims = await GetClaimsAsync(_openClient, record);

        claims.GetProperty("userType").GetString().ShouldBe("dealer");
        claims.GetProperty("hasCari").GetBoolean().ShouldBeTrue();
        claims.GetProperty("hasBanka").GetBoolean().ShouldBeFalse();
        claims.GetProperty("branchIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([BranchA]);
        claims.GetProperty("canA").GetBoolean().ShouldBeTrue();
        claims.GetProperty("canB").GetBoolean().ShouldBeFalse();
        claims.GetProperty("maxDiscount").GetDecimal().ShouldBe(12.5m);
        claims.GetProperty("cariId").GetGuid().ShouldBe(cariId);
    }

    [Test]
    public async Task Claims_NullBranchIds_MeansAllAndCanAccessAnyBranch()
    {
        var claims = await GetClaimsAsync(_defaultClient, NewRecord() with { BranchIds = null });

        claims.GetProperty("branchIds").ValueKind.ShouldBe(JsonValueKind.Null);
        claims.GetProperty("canA").GetBoolean().ShouldBeTrue();
        claims.GetProperty("canOther").GetBoolean().ShouldBeTrue();
        claims.GetProperty("cariId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Claims_EmptyBranchIds_MeansNoAccess()
    {
        var claims = await GetClaimsAsync(_defaultClient, NewRecord() with { BranchIds = [] });

        claims.GetProperty("branchIds").GetArrayLength().ShouldBe(0);
        claims.GetProperty("canA").GetBoolean().ShouldBeFalse();
        claims.GetProperty("canOther").GetBoolean().ShouldBeFalse();
    }
}
