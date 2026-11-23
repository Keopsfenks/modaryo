using System.Net;
using System.Text;
using ERP.Application.Common.Interfaces;
using ERP.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Modaryo.Internal;

namespace ERP.Web.FunctionalTests.Platform;

public class BranchDirectoryTests : TestBase {
    private const string OutboundSecret = "erp-to-identity-test-secret";

    private static readonly Guid Tenant       = Guid.Parse("0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b21");
    private static readonly Guid OtherTenant  = Guid.Parse("0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b22");
    private static readonly Guid DefaultId    = Guid.Parse("0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b31");
    private static readonly Guid ActiveId     = Guid.Parse("0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b32");
    private static readonly Guid InactiveId   = Guid.Parse("0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b33");

    private sealed class FakeIdentityHandler : HttpMessageHandler {
        public List<(HttpMethod Method, string PathAndQuery, Dictionary<string, string> Headers)> Calls { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery,
                       request.Headers.ToDictionary(h => h.Key, h => h.Value.First())));
            var json = $$"""
                         [
                           {"id":"{{DefaultId}}","code":"MRK","name":"Merkez","isActive":true,"isDefault":true},
                           {"id":"{{ActiveId}}","code":"S1","name":"Şube 1","isActive":true,"isDefault":false},
                           {"id":"{{InactiveId}}","code":"S2","name":"Şube 2","isActive":false,"isDefault":false}
                         ]
                         """;
            return Task.FromResult(new HttpResponseMessage(Status) {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private static (IdentityBranchClient Client, FakeIdentityHandler Handler) CreateClient() {
        var handler = new FakeIdentityHandler();
        var signing = new InternalHttpClientHandler("erp", OutboundSecret) { InnerHandler = handler };
        var http    = new HttpClient(signing) { BaseAddress = new Uri("http://identity.test") };
        return (new IdentityBranchClient(http, new MemoryCache(new MemoryCacheOptions())), handler);
    }

    [Test]
    public async Task BranchDirectory_ReturnsBranchesFromIdentity() {
        var (client, handler) = CreateClient();

        var list = await client.ListAsync(Tenant, CancellationToken.None);

        list.ShouldBe([
                          new BranchRef(DefaultId, "MRK", "Merkez", true, true),
                          new BranchRef(ActiveId, "S1", "Şube 1", true, false),
                          new BranchRef(InactiveId, "S2", "Şube 2", false, false)
                      ]);
        var call = handler.Calls.Single();
        call.Method.ShouldBe(HttpMethod.Get);
        call.PathAndQuery.ShouldBe($"/internal/tenants/{Tenant}/branches");
    }

    [Test]
    public async Task BranchDirectory_RequestIsSignedAndCarriesTenant() {
        var (client, handler) = CreateClient();

        await client.ListAsync(Tenant, CancellationToken.None);

        var call = handler.Calls.Single();
        call.Headers[InternalHeaders.Service].ShouldBe("erp");
        call.Headers[InternalHeaders.Tenant].ShouldBe(Tenant.ToString("D"));
        var timestamp = long.Parse(call.Headers[InternalHeaders.Timestamp]);
        InternalSignature.Verify(OutboundSecret, "GET", call.PathAndQuery, timestamp, Tenant.ToString("D"), null, [],
                                 call.Headers[InternalHeaders.Signature]).ShouldBeTrue();
        InternalSignature.Verify("baska-sir-0123456789", "GET", call.PathAndQuery, timestamp, Tenant.ToString("D"), null, [],
                                 call.Headers[InternalHeaders.Signature]).ShouldBeFalse();
    }

    [Test]
    public async Task ExistsActive_ActiveBranch_True() {
        var (client, _) = CreateClient();

        (await client.ExistsActiveAsync(Tenant, ActiveId, CancellationToken.None)).ShouldBeTrue();
        (await client.ExistsActiveAsync(Tenant, DefaultId, CancellationToken.None)).ShouldBeTrue();
    }

    [Test]
    public async Task ExistsActive_InactiveBranch_False() {
        var (client, _) = CreateClient();

        (await client.ExistsActiveAsync(Tenant, InactiveId, CancellationToken.None)).ShouldBeFalse();
        (await client.ExistsActiveAsync(Tenant, Guid.NewGuid(), CancellationToken.None)).ShouldBeFalse();
    }

    [Test]
    public async Task BranchDirectory_CachesPerTenant() {
        var (client, handler) = CreateClient();

        await client.ListAsync(Tenant, CancellationToken.None);
        await client.ExistsActiveAsync(Tenant, ActiveId, CancellationToken.None);
        handler.Calls.Count.ShouldBe(1);

        await client.ListAsync(OtherTenant, CancellationToken.None);
        handler.Calls.Count.ShouldBe(2);
    }

    [Test]
    public async Task BranchDirectory_IdentityError_ThrowsAndIsNotCached() {
        var (client, handler) = CreateClient();
        handler.Status = HttpStatusCode.InternalServerError;

        await Should.ThrowAsync<HttpRequestException>(() => client.ListAsync(Tenant, CancellationToken.None));

        handler.Status = HttpStatusCode.OK;
        (await client.ListAsync(Tenant, CancellationToken.None)).Count.ShouldBe(3);
    }

    // --- Kimlik servisinin çağırdığı kullanım ucu ---

    private sealed class StubUsageProvider(bool inUse) : IBranchUsageProvider {
        public List<(Guid Tenant, Guid Branch)> Calls { get; } = [];

        public Task<bool> IsInUseAsync(Guid tenantId, Guid branchId, CancellationToken ct) {
            Calls.Add((tenantId, branchId));
            return Task.FromResult(inUse);
        }
    }

    private static HttpRequestMessage SignedUsage(Guid branchId, Guid? tenant, string caller = InternalProbes.IdentityCaller,
                                                  string secret = InternalProbes.IdentitySecret) {
        var path    = $"/internal/branches/{branchId}/usage";
        var ts      = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tenantS = tenant?.ToString("D");
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(InternalHeaders.Service,   caller);
        request.Headers.Add(InternalHeaders.Timestamp, ts.ToString());
        request.Headers.Add(InternalHeaders.Signature,
                            InternalSignature.Compute(secret, "GET", path, ts, tenantS, null, []));
        if (tenantS is not null) request.Headers.Add(InternalHeaders.Tenant, tenantS);
        return request;
    }

    [Test]
    public async Task BranchUsage_NoProviders_ReturnsNotInUse() {
        using var client   = Factory.CreateClient();
        using var response = await client.SendAsync(SignedUsage(ActiveId, Tenant));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("inUse").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task BranchUsage_AnyProviderTrue_ReturnsInUse_WithTenantFromHeader() {
        var unused = new StubUsageProvider(false);
        var used   = new StubUsageProvider(true);
        await using var factory = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => {
                                                                  s.AddSingleton<IBranchUsageProvider>(unused);
                                                                  s.AddSingleton<IBranchUsageProvider>(used);
                                                              }));
        using var client   = factory.CreateClient();
        using var response = await client.SendAsync(SignedUsage(ActiveId, Tenant));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("inUse").GetBoolean().ShouldBeTrue();
        used.Calls.ShouldBe([(Tenant, ActiveId)]);
    }

    [Test]
    public async Task BranchUsage_AllProvidersFalse_ReturnsNotInUse() {
        await using var factory = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => {
                                                                  s.AddSingleton<IBranchUsageProvider>(new StubUsageProvider(false));
                                                              }));
        using var client   = factory.CreateClient();
        using var response = await client.SendAsync(SignedUsage(ActiveId, Tenant));

        (await response.JsonAsync()).GetProperty("inUse").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task BranchUsage_OtherCallerOrBadSignature_Rejected() {
        using var client = Factory.CreateClient();

        using var wrongSecret = await client.SendAsync(SignedUsage(ActiveId, Tenant, secret: "yanlis-sir-0123456789"));
        using var limited     = await client.SendAsync(SignedUsage(ActiveId, Tenant, InternalProbes.LimitedCaller, InternalProbes.OtherSecret));
        using var noTenant    = await client.SendAsync(SignedUsage(ActiveId, null));

        wrongSecret.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        limited.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        noTenant.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BranchUsage_IsNotInOpenApi() {
        using var client   = Factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        (await response.Content.ReadAsStringAsync()).ShouldNotContain("/internal/");
    }
}
