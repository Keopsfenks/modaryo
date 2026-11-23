using System.Text;
using ERP.Domain.Platform;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modaryo.Internal;

namespace ERP.Web.FunctionalTests.Platform;

public class InternalApiTests : TestBase {
    private static readonly Guid Tenant = Guid.Parse("0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b21");

    [SetUp]
    public void ResetProbes() {
        InternalProbes.Reset();
    }

    private static HttpRequestMessage Signed(HttpMethod method,
                                             string     pathAndQuery,
                                             string?    body           = null,
                                             Guid?      tenant         = null,
                                             string?    idempotencyKey = null,
                                             long?      timestamp      = null,
                                             string     caller         = InternalProbes.TestCaller,
                                             string     secret         = InternalProbes.Secret,
                                             string?    signedTenant   = "same",
                                             string?    signedBody     = null,
                                             string?    signedKey      = "same"
    ) {
        var ts      = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tenantS = tenant?.ToString("D");
        var request = new HttpRequestMessage(method, pathAndQuery);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var signature = InternalSignature.Compute(secret, method.Method, pathAndQuery, ts,
                                                  signedTenant == "same" ? tenantS : signedTenant,
                                                  signedKey == "same" ? idempotencyKey : signedKey,
                                                  Encoding.UTF8.GetBytes(signedBody ?? body ?? ""));
        request.Headers.Add(InternalHeaders.Service,   caller);
        request.Headers.Add(InternalHeaders.Timestamp, ts.ToString());
        request.Headers.Add(InternalHeaders.Signature, signature);
        if (tenantS is not null) request.Headers.Add(InternalHeaders.Tenant, tenantS);
        if (idempotencyKey is not null) request.Headers.Add(InternalHeaders.IdempotencyKey, idempotencyKey);
        return request;
    }

    private static HttpClient Client() {
        return Factory.CreateClient();
    }

    [Test]
    public async Task Internal_ValidSignature_Returns200WithServiceAndTenant() {
        using var client   = Client();
        using var request  = Signed(HttpMethod.Get, "/internal/ping?x=1", tenant: Tenant);
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("service").GetString().ShouldBe("test");
        body.GetProperty("tenant").GetGuid().ShouldBe(Tenant);
    }

    [Test]
    public async Task Internal_PingWithoutTenant_Returns200WithNullTenant() {
        using var client   = Client();
        using var request  = Signed(HttpMethod.Get, "/internal/ping");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("tenant").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Test]
    public async Task Internal_StaleTimestamp_Returns401() {
        using var client   = Client();
        var       stale    = DateTimeOffset.UtcNow.AddSeconds(-301).ToUnixTimeSeconds();
        using var request  = Signed(HttpMethod.Get, "/internal/ping", tenant: Tenant, timestamp: stale);
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ErrorCodeAsync()).ShouldBe("internal_unauthorized");
    }

    [Test]
    public async Task Internal_FutureTimestampBeyondSkew_Returns401() {
        using var client   = Client();
        var       future   = DateTimeOffset.UtcNow.AddSeconds(400).ToUnixTimeSeconds();
        using var request  = Signed(HttpMethod.Get, "/internal/ping", tenant: Tenant, timestamp: future);
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Internal_TamperedBody_Returns401() {
        using var client   = Client();
        using var request  = Signed(HttpMethod.Post, "/internal/test/counter", body: "{\"a\":2}", tenant: Tenant,
                                    idempotencyKey: "k1", signedBody: "{\"a\":1}");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        InternalProbes.Counter.ShouldBe(0);
    }

    [Test]
    public async Task Internal_TamperedTenantHeader_Returns401() {
        using var client = Client();
        // İmza başka bir firma için atıldı, başlık başka firmayı söylüyor.
        using var request = Signed(HttpMethod.Get, "/internal/ping", tenant: Tenant,
                                   signedTenant: Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Internal_TamperedIdempotencyKey_Returns401() {
        using var client = Client();
        // İmza "k-signed" için atıldı, istek başka anahtarla gönderiliyor.
        using var request = Signed(HttpMethod.Post, "/internal/test/counter", body: "{}", tenant: Tenant,
                                   idempotencyKey: "k-sent", signedKey: "k-signed");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        InternalProbes.Counter.ShouldBe(0);
    }

    [Test]
    public async Task Internal_OversizeContentLength_Returns413() {
        using var client  = Client();
        var       big     = new string('a', 1_048_576 + 1);
        using var request = Signed(HttpMethod.Post, "/internal/test/counter", body: big, tenant: Tenant, idempotencyKey: "big-1");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await response.ErrorCodeAsync()).ShouldBe("internal_body_too_large");
        InternalProbes.Counter.ShouldBe(0);
    }

    [Test]
    public async Task Internal_UnknownCallerWithBigBody_Returns401WithoutReadingBody() {
        using var client  = Client();
        var       big     = new string('a', 1_048_576 + 1);
        using var request = Signed(HttpMethod.Post, "/internal/test/counter", body: big, tenant: Tenant, idempotencyKey: "big-2", caller: "nobody");
        using var response = await client.SendAsync(request);

        // Gövde sınırı kontrolünden (413) önce çağıran doğrulanır: bilinmeyen çağıran genel 401 alır.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Internal_StalePendingReservation_IsTakenOver() {
        var body = "{\"a\":1}";
        await using (var scope = Factory.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.IdempotencyRecords.Add(new IdempotencyRecord {
                Caller = "test", Key = "stale-1", TenantId = Tenant,
                RequestHash = InternalAuthMiddleware.RequestHash("POST", "/internal/test/counter", Tenant.ToString("D"), Encoding.UTF8.GetBytes(body)),
                Status = 0, ResponseJson = "", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            });
            await db.SaveChangesAsync();
        }

        using var client = Client();
        using var request = Signed(HttpMethod.Post, "/internal/test/counter", body: body, tenant: Tenant, idempotencyKey: "stale-1");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        InternalProbes.Counter.ShouldBe(1);
    }

    [Test]
    public async Task Internal_ParallelIdenticalWrites_ExecuteOnce() {
        using var client = Client();
        var tasks = Enumerable.Range(0, 8).Select(async _ => {
                                                      using var request = Signed(HttpMethod.Post, "/internal/test/counter", body: "{\"a\":1}",
                                                                                 tenant: Tenant, idempotencyKey: "race-1");
                                                      using var response = await client.SendAsync(request);
                                                      var code = response.StatusCode == HttpStatusCode.Conflict
                                                                     ? await response.ErrorCodeAsync()
                                                                     : null;
                                                      return (response.StatusCode, code);
                                                  });
        var results = await Task.WhenAll(tasks);

        InternalProbes.Counter.ShouldBe(1);
        results.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK
                                 || (r.StatusCode == HttpStatusCode.Conflict && r.code == "idempotency_in_progress"));
        results.ShouldContain(r => r.StatusCode == HttpStatusCode.OK);
    }

    [Test]
    public async Task Internal_UnknownCallerOrWrongSecretOrMissingHeaders_Return401() {
        using var client = Client();

        using (var unknown = Signed(HttpMethod.Get, "/internal/ping", tenant: Tenant, caller: "nobody"))
            (await client.SendAsync(unknown)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using (var wrong = Signed(HttpMethod.Get, "/internal/ping", tenant: Tenant, secret: "yanlis"))
            (await client.SendAsync(wrong)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using (var missing = new HttpRequestMessage(HttpMethod.Get, "/internal/ping"))
            (await client.SendAsync(missing)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Internal_RouteNotAllowedForCaller_Returns403() {
        using var client = Client();

        using var request  = Signed(HttpMethod.Get, "/internal/other/x", tenant: Tenant);
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).ShouldBe("internal_forbidden");

        // "limited" çağıranı yalnızca ping'e erişebilir.
        using var limited = Signed(HttpMethod.Get, "/internal/test/whoami", tenant: Tenant,
                                   caller: InternalProbes.LimitedCaller, secret: InternalProbes.OtherSecret);
        (await client.SendAsync(limited)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Internal_MissingOrInvalidTenant_Returns400ExceptPing() {
        using var client = Client();

        using var missing = Signed(HttpMethod.Get, "/internal/test/whoami");
        (await client.SendAsync(missing)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Geçerli imza ama Guid olmayan tenant.
        var ts      = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var bad = new HttpRequestMessage(HttpMethod.Get, "/internal/test/whoami");
        bad.Headers.Add(InternalHeaders.Service,   "test");
        bad.Headers.Add(InternalHeaders.Timestamp, ts.ToString());
        bad.Headers.Add(InternalHeaders.Tenant,    "bozuk");
        bad.Headers.Add(InternalHeaders.Signature,
                        InternalSignature.Compute(InternalProbes.Secret, "GET", "/internal/test/whoami", ts, "bozuk", null, []));
        (await client.SendAsync(bad)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Internal_NotReachableWithSessionCookieOnly_Returns401() {
        var sid = await ContractSessions.StoreAsync(ContractSessions.Example());
        using var client = Factory.CreateClientWithSession(sid);

        (await client.GetAsync("/internal/ping")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/internal/test/whoami")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Internal_SignedRequestWithSessionCookie_UsesServicePrincipalOnly() {
        var sid = await ContractSessions.StoreAsync(ContractSessions.Example());
        using var client  = Client();
        using var request = Signed(HttpMethod.Get, "/internal/test/whoami", tenant: Tenant);
        request.Headers.Add("Cookie", $"modaryo_sid={sid}");

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("tenantId").GetGuid().ShouldBe(Tenant);
        body.GetProperty("userId").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        body.GetProperty("userFullName").GetString().ShouldBe("sistem:test");
        body.GetProperty("hasPos").GetBoolean().ShouldBeFalse();
        body.GetProperty("branchIds").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Internal_PostWithoutCsrfHeader_IsNotBlocked() {
        using var client = Client();
        using var request = Signed(HttpMethod.Post, "/internal/test/counter", body: "{}", tenant: Tenant,
                                   idempotencyKey: "csrf-1");

        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Internal_WriteWithoutIdempotencyKey_Returns400() {
        using var client   = Client();
        using var request  = Signed(HttpMethod.Post, "/internal/test/counter", body: "{}", tenant: Tenant);
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).ShouldBe("idempotency_key_required");
        InternalProbes.Counter.ShouldBe(0);
    }

    [Test]
    public async Task Internal_SameIdempotencyKey_ExecutesOnce() {
        using var client = Client();

        using var first  = Signed(HttpMethod.Post, "/internal/test/counter", body: "{\"a\":1}", tenant: Tenant, idempotencyKey: "k1");
        using var second = Signed(HttpMethod.Post, "/internal/test/counter", body: "{\"a\":1}", tenant: Tenant, idempotencyKey: "k1");
        using var r1 = await client.SendAsync(first);
        using var r2 = await client.SendAsync(second);

        r1.StatusCode.ShouldBe(HttpStatusCode.OK);
        r2.StatusCode.ShouldBe(HttpStatusCode.OK);
        var b1 = await r1.JsonAsync();
        var b2 = await r2.JsonAsync();
        b1.GetProperty("count").GetInt32().ShouldBe(1);
        b1.GetProperty("body").GetString().ShouldBe("{\"a\":1}"); // gövde imza için okunduktan sonra geri sarıldı
        b2.GetProperty("count").GetInt32().ShouldBe(1);
        InternalProbes.Counter.ShouldBe(1);
    }

    [Test]
    public async Task Internal_SameKeyDifferentBody_Returns409() {
        using var client = Client();

        using var first  = Signed(HttpMethod.Post, "/internal/test/counter", body: "{\"a\":1}", tenant: Tenant, idempotencyKey: "k2");
        using var second = Signed(HttpMethod.Post, "/internal/test/counter", body: "{\"a\":2}", tenant: Tenant, idempotencyKey: "k2");
        (await client.SendAsync(first)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var response = await client.SendAsync(second);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("idempotency_conflict");
        InternalProbes.Counter.ShouldBe(1);
    }

    [Test]
    public async Task Internal_SameKeyDifferentCallerOrTenant_AreIndependentOrConflict() {
        using var client = Client();

        using var first = Signed(HttpMethod.Post, "/internal/test/counter", body: "{}", tenant: Tenant, idempotencyKey: "k3");
        (await client.SendAsync(first)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Aynı çağıran + anahtar, başka firma: istek özeti farklı olduğundan çakışma.
        using var otherTenant = Signed(HttpMethod.Post, "/internal/test/counter", body: "{}", tenant: Guid.NewGuid(), idempotencyKey: "k3");
        (await client.SendAsync(otherTenant)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        InternalProbes.Counter.ShouldBe(1);
    }

    [Test]
    public async Task Internal_ServerError_ReleasesKeySoRetryRuns() {
        using var client = Client();

        using var first = Signed(HttpMethod.Post, "/internal/test/flaky", body: "{}", tenant: Tenant, idempotencyKey: "k4");
        (await client.SendAsync(first)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        using var retry = Signed(HttpMethod.Post, "/internal/test/flaky", body: "{}", tenant: Tenant, idempotencyKey: "k4");
        using var response = await client.SendAsync(retry);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("ok").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task Internal_SameKeyWhileStillProcessing_Returns409() {
        using var client = Client();
        using var first  = Signed(HttpMethod.Post, "/internal/test/slow", body: "{}", tenant: Tenant, idempotencyKey: "k5");
        var       firstTask = client.SendAsync(first);
        await InternalProbes.SlowEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var second = Signed(HttpMethod.Post, "/internal/test/slow", body: "{}", tenant: Tenant, idempotencyKey: "k5");
        using var conflict = await client.SendAsync(second);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await conflict.ErrorCodeAsync()).ShouldBe("idempotency_in_progress");
        conflict.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(1));

        // Aynı anahtar, farklı gövde: işlenirken bile ayrı kod.
        using var different = Signed(HttpMethod.Post, "/internal/test/slow", body: "{\"x\":1}", tenant: Tenant, idempotencyKey: "k5");
        using var differentResponse = await client.SendAsync(different);
        differentResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await differentResponse.ErrorCodeAsync()).ShouldBe("idempotency_conflict");

        InternalProbes.SlowRelease.SetResult();
        using var done = await firstTask;
        done.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task IdempotencyStore_TakenOverReservation_IgnoresLateCompleteAndReleaseOfOldOwner() {
        var timeout = TimeSpan.FromMinutes(5);
        var caller  = "test";

        await using var scopeA = Factory.Services.CreateAsyncScope();
        await using var scopeB = Factory.Services.CreateAsyncScope();
        var storeA = scopeA.ServiceProvider.GetRequiredService<IdempotencyStore>();
        var storeB = scopeB.ServiceProvider.GetRequiredService<IdempotencyStore>();
        var db     = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var a = await storeA.ReserveAsync(caller, "own-1", Tenant, "h", timeout, CancellationToken.None);
        a.Outcome.ShouldBe(IdempotencyOutcome.Reserved);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE idempotency_records SET \"CreatedAt\" = now() - interval '10 minutes' WHERE \"Key\" = 'own-1'");

        var b = await storeB.ReserveAsync(caller, "own-1", Tenant, "h", timeout, CancellationToken.None);
        b.Outcome.ShouldBe(IdempotencyOutcome.Reserved);
        b.OwnerToken.ShouldNotBe(a.OwnerToken);

        // Eski sahibin geç gelen tamamlama / serbest bırakması yeni sahibin kaydına dokunmaz.
        await storeA.CompleteAsync(caller, "own-1", a.OwnerToken, 200, "{\"old\":true}", CancellationToken.None);
        await storeA.ReleaseAsync(caller, "own-1", a.OwnerToken, CancellationToken.None);
        var record = await db.IdempotencyRecords.AsNoTracking().SingleAsync(r => r.Key == "own-1");
        record.Status.ShouldBe(0);
        record.ResponseJson.ShouldBe("");
        record.OwnerToken.ShouldBe(b.OwnerToken);

        await storeB.CompleteAsync(caller, "own-1", b.OwnerToken, 201, "{\"new\":true}", CancellationToken.None);
        record = await db.IdempotencyRecords.AsNoTracking().SingleAsync(r => r.Key == "own-1");
        record.Status.ShouldBe(201);
        record.ResponseJson.ShouldBe("{\"new\":true}");
    }

    [Test]
    public async Task IdempotencyStore_Purge_RemovesOnlyRecordsOlderThanSevenDays() {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db    = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IdempotencyStore>();
        var now   = DateTimeOffset.UtcNow;
        db.IdempotencyRecords.AddRange(
                                       Record("old", now.AddDays(-8)),
                                       Record("fresh", now.AddDays(-6)));
        await db.SaveChangesAsync();

        var removed = await store.PurgeAsync(CancellationToken.None);

        removed.ShouldBe(1);
        db.IdempotencyRecords.Select(r => r.Key).ToList().ShouldBe(["fresh"]);
    }

    private static IdempotencyRecord Record(string key, DateTimeOffset createdAt) {
        return new IdempotencyRecord {
            Caller = "test", Key = key, TenantId = Tenant, RequestHash = "h", Status = 200,
            ResponseJson = "{}", CreatedAt = createdAt
        };
    }
}
