using System.Text;
using System.Text.Json;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Platform;
using ERP.Domain.Treasury;
using ERP.Infrastructure.Data;
using ERP.Web.FunctionalTests.Platform;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modaryo.Internal;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Treasury.PaymentSupport;

namespace ERP.Web.FunctionalTests.Treasury;

public class JournalVoucherTests : TestBase {
    private const string Vouchers = $"{Base}/journal-vouchers";
    private const string Internal = "/internal/journal-vouchers";

    [SetUp]
    public void ResetProbes() {
        InternalProbes.Reset();
    }

    private static object Line(string code, decimal debit, decimal credit, Guid? cari = null, string? description = null) {
        return new { accountCode = code, debit, credit, description, currentAccountId = cari };
    }

    private static Task<HttpResponseMessage> Post(Env env, object[] lines, DateOnly? date = null, string description = "Maaş tahakkuku") {
        return env.Client.PostAsJsonAsync(Vouchers, new { date = (date ?? Today).ToString("yyyy-MM-dd"), description, lines });
    }

    private static string InternalBody(object[] lines, Guid sourceId, DateOnly? date = null, string sourceType = "payroll") {
        return JsonSerializer.Serialize(new { date = (date ?? Today).ToString("yyyy-MM-dd"), description = "Aralık bordrosu", sourceType, sourceId, lines });
    }

    private static HttpRequestMessage Signed(Guid tenant, string body, string? key, string caller = InternalProbes.HrCaller, string secret = InternalProbes.HrSecret) {
        var ts      = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var request = new HttpRequestMessage(HttpMethod.Post, Internal) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add(InternalHeaders.Service, caller);
        request.Headers.Add(InternalHeaders.Timestamp, ts.ToString());
        request.Headers.Add(InternalHeaders.Tenant, tenant.ToString("D"));
        if (key is not null) request.Headers.Add(InternalHeaders.IdempotencyKey, key);
        request.Headers.Add(InternalHeaders.Signature, InternalSignature.Compute(secret, "POST", Internal, ts, tenant.ToString("D"), key, Encoding.UTF8.GetBytes(body)));
        return request;
    }

    private static async Task<HttpResponseMessage> SendInternal(Guid tenant, string body, string? key, string caller = InternalProbes.HrCaller, string secret = InternalProbes.HrSecret) {
        using var client  = TestEnvironment.Factory.CreateClient();
        using var request = Signed(tenant, body, key, caller, secret);
        return await client.SendAsync(request);
    }

    private static async Task<List<JournalVoucher>> StoredVouchers(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.JournalVouchers.AsNoTracking().OrderBy(v => v.Number).ToListAsync();
    }

    private static async Task<List<OutboxMessage>> Outbox(Guid tenant, string type) {
        await using var scope = TestEnvironment.Factory.Services.CreateAsyncScope();
        var             db    = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.OutboxMessages.IgnoreQueryFilters().AsNoTracking().Where(m => m.TenantId == tenant && m.Type == type).OrderBy(m => m.Id).ToListAsync();
    }

    private static async Task<IdempotencyRecord> Record(string key) {
        await using var scope = TestEnvironment.Factory.Services.CreateAsyncScope();
        var             db    = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.IdempotencyRecords.AsNoTracking().SingleAsync(r => r.Caller == InternalProbes.HrCaller && r.Key == key);
    }

    [Test]
    public async Task Unbalanced_Returns400() {
        var env = await NewEnv();

        using var response = await Post(env, [Line("770", 100m, 0m), Line("100", 0m, 90m)]);

        await Fail(response, HttpStatusCode.BadRequest, "voucher_unbalanced");
        (await StoredVouchers(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task InvalidLines_Return400() {
        var env = await NewEnv();

        using (var oneLine = await Post(env, [Line("770", 100m, 0m)])) oneLine.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var both = await Post(env, [Line("770", 100m, 50m), Line("100", 0m, 50m)])) both.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var zero = await Post(env, [Line("770", 0m, 0m), Line("100", 0m, 0m)])) zero.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var noCode = await Post(env, [Line("", 10m, 0m), Line("100", 0m, 10m)])) noCode.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var scale = await Post(env, [Line("770", 10.005m, 0m), Line("100", 0m, 10.005m)])) scale.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredVouchers(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task Balanced_Returns201WithNumber() {
        var env = await NewEnv();

        using var response = await Post(env, [Line("770", 100m, 0m), Line("100", 0m, 100m)]);
        var       body     = await Created(response);

        body.GetProperty("number").GetString().ShouldBe("MH2026000001");
        var voucher = (await StoredVouchers(env.Tenant)).Single();
        (voucher.SourceType, voucher.ExternalCaller, voucher.Lines.Count, voucher.IsBalanced).ShouldBe(("Manual", null, 2, true));
        (await Outbox(env.Tenant, "journal_voucher.posted")).Count.ShouldBe(1);

        using var get = await env.Client.GetAsync($"{Vouchers}/{body.GetProperty("id").GetGuid()}");
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await get.JsonAsync();
        (dto.GetProperty("totalDebit").GetDecimal(), dto.GetProperty("totalCredit").GetDecimal()).ShouldBe((100m, 100m));
        using var list = await env.Client.GetAsync($"{Vouchers}?from=2026-03-01&to=2026-03-31");
        (await list.JsonAsync()).GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task LineWithCari_PostsToCariLedger() {
        var env = await NewEnv();

        // Bordro: personel carisi (335) alacaklanır; gider borçlanır.
        using var response = await Post(env, [Line("770", 1000m, 0m), Line("335", 0m, 1000m, env.Supplier, "Ocak maaşı")]);
        var       body     = await Created(response);

        (await CariBalance(env, env.Supplier)).ShouldBe(-1000m);
        var entry = (await Entries(env.Tenant)).Single();
        (entry.Source, entry.SourceId, entry.Credit, entry.DocumentNo, entry.CurrentAccountId).ShouldBe((LedgerEntrySource.Voucher, body.GetProperty("id").GetGuid(), 1000m, "MH2026000001", env.Supplier));

        // Aynı cariye borç satırı otomatik olarak alacakla eşleşir.
        using var second = await Post(env, [Line("335", 400m, 0m, env.Supplier), Line("100", 0m, 400m)]);
        await Created(second);
        (await CariBalance(env, env.Supplier)).ShouldBe(-600m);
        (await Allocations(env.Tenant)).Sum(a => a.Amount).ShouldBe(400m);
    }

    [Test]
    public async Task LineWithForeignOrUnknownCari_Returns400Or404() {
        var env = await NewEnv();
        var usdCari = await CreateCari(env.Client, "USD1", customer: true, supplier: false, currency: "USD", name: "Dolar Müşteri");

        using var foreign = await Post(env, [Line("120", 10m, 0m, usdCari), Line("600", 0m, 10m)]);
        await Fail(foreign, HttpStatusCode.BadRequest, "voucher_cari_currency");
        using var unknown = await Post(env, [Line("120", 10m, 0m, Guid.NewGuid()), Line("600", 0m, 10m)]);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StoredVouchers(env.Tenant)).ShouldBeEmpty();
        (await Entries(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task Cancel_ReversesCariLines() {
        var env = await NewEnv();
        using var created = await Post(env, [Line("770", 1000m, 0m), Line("335", 0m, 1000m, env.Supplier)]);
        var       id      = (await Created(created)).GetProperty("id").GetGuid();
        (await CariBalance(env, env.Supplier)).ShouldBe(-1000m);

        using var cancel = await env.Client.PostAsJsonAsync($"{Vouchers}/{id}/cancel", new { reason = "Hatalı bordro" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        (await CariBalance(env, env.Supplier)).ShouldBe(0m);
        var voucher = (await StoredVouchers(env.Tenant)).Single();
        (voucher.IsCancelled, voucher.CancelReason, voucher.CancelledAt is not null).ShouldBe((true, "Hatalı bordro", true));
        (await Entries(env.Tenant)).Single().IsCancelled.ShouldBeTrue();
        (await Outbox(env.Tenant, "journal_voucher.cancelled")).Count.ShouldBe(1);

        using var again = await env.Client.PostAsJsonAsync($"{Vouchers}/{id}/cancel", new { reason = "Tekrar" });
        await Fail(again, HttpStatusCode.Conflict, "voucher_cancelled");
        using var missing = await env.Client.PostAsJsonAsync($"{Vouchers}/{Guid.NewGuid()}/cancel", new { reason = "Yok" });
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Manual_PeriodLocked_AndCancelInLockedPeriod_Return409() {
        var env = await NewEnv();
        using var created = await Post(env, [Line("770", 10m, 0m), Line("100", 0m, 10m)], new DateOnly(2026, 2, 10));
        var       id      = (await Created(created)).GetProperty("id").GetGuid();
        await LockPeriod(env.Client, "2026-02-28");

        using var locked = await Post(env, [Line("770", 10m, 0m), Line("100", 0m, 10m)], new DateOnly(2026, 2, 20));
        await Fail(locked, HttpStatusCode.Conflict, "period_locked");
        using var cancel = await env.Client.PostAsJsonAsync($"{Vouchers}/{id}/cancel", new { reason = "Kilitli dönem" });
        await Fail(cancel, HttpStatusCode.Conflict, "period_locked");
    }

    [Test]
    public async Task Permissions_ManageRequired_ReadAllowsListing() {
        var env      = await NewEnv();
        var readOnly = await OtherClient(env, "finans.read");

        using var denied = await readOnly.PostAsJsonAsync(Vouchers, new { date = "2026-03-12", description = "x", lines = new[] { Line("770", 1m, 0m), Line("100", 0m, 1m) } });
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var list = await readOnly.GetAsync(Vouchers);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var noAccess = await OtherClient(env, "cari.read");
        using var forbiddenList = await noAccess.GetAsync(Vouchers);
        forbiddenList.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Internal_IdempotentAndActorIsSistemHr() {
        var env    = await NewEnv();
        var source = Guid.NewGuid();
        var body   = InternalBody([Line("770", 1000m, 0m), Line("335", 0m, 1000m, env.Supplier)], source);
        var key    = "payroll-" + Guid.NewGuid().ToString("N");

        using var first = await SendInternal(env.Tenant, body, key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var created = await first.JsonAsync();

        // Aynı imzalı istek yeniden gönderilirse (tekrar saldırısı ya da ağ yeniden denemesi) kayıtlı yanıt döner; ikinci fiş yazılmaz.
        using var replay = await SendInternal(env.Tenant, body, key);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await replay.JsonAsync()).GetProperty("id").GetGuid().ShouldBe(created.GetProperty("id").GetGuid());

        var voucher = (await StoredVouchers(env.Tenant)).Single();
        (voucher.ExternalCaller, voucher.SourceType, voucher.SourceId, voucher.Number).ShouldBe(("hr", "payroll", source, created.GetProperty("number").GetString()));
        (await Entries(env.Tenant)).Count.ShouldBe(1);
        (await CariBalance(env, env.Supplier)).ShouldBe(-1000m);
        (await Outbox(env.Tenant, "journal_voucher.posted")).Single().Actor.ShouldBe("sistem:hr");

        // Aynı anahtar başka bir gövdeyle kullanılamaz; anahtarsız yazma isteği reddedilir.
        var other = InternalBody([Line("770", 5m, 0m), Line("100", 0m, 5m)], Guid.NewGuid());
        using var conflict = await SendInternal(env.Tenant, other, key);
        await Fail(conflict, HttpStatusCode.Conflict, "idempotency_conflict");
        using var noKey = await SendInternal(env.Tenant, other, null);
        await Fail(noKey, HttpStatusCode.BadRequest, "idempotency_key_required");
        (await StoredVouchers(env.Tenant)).Count.ShouldBe(1);
    }

    [Test]
    public async Task Internal_SignedRequest_ThroughMediatrToOutbox_WithSystemActor() {
        // Çapraz görev: imzalı iç istek -> iç API middleware'i -> MediatR işleyici -> fiş + cari + outbox, hepsi "sistem:{servis}" eylemcisiyle.
        var env = await NewEnv();
        var key = "assets-" + Guid.NewGuid().ToString("N");
        var body = InternalBody([Line("770", 250m, 0m), Line("335", 0m, 250m, env.Supplier)], Guid.NewGuid(), sourceType: "depreciation");

        using var response = await SendInternal(env.Tenant, body, key, InternalProbes.AssetsCaller, InternalProbes.AssetsSecret);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await StoredVouchers(env.Tenant)).Single().ExternalCaller.ShouldBe("assets");
        var messages = await Outbox(env.Tenant, "journal_voucher.posted");
        messages.Single().Actor.ShouldBe("sistem:assets");
        JsonDocument.Parse(messages.Single().PayloadJson).RootElement.GetProperty("external_caller").GetString().ShouldBe("assets");
        (await Entries(env.Tenant)).Single().Source.ShouldBe(LedgerEntrySource.Voucher);
    }

    [Test]
    public async Task Internal_IdempotencyCompletedInHandlerTransaction_SurvivesLateCompleteFailure() {
        var env = await NewEnv();
        var key = "atomic-" + Guid.NewGuid().ToString("N");
        var body = InternalBody([Line("770", 70m, 0m), Line("335", 0m, 70m, env.Supplier)], Guid.NewGuid());
        InternalProbes.FailLateComplete = true;
        try {
            using var first = await SendInternal(env.Tenant, body, key);
            first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
            var created = await first.JsonAsync();

            // Middleware'in yanıt sonrası tamamlaması bozuldu; kayıt yine de tamamlanmış çünkü işleyici kendi işleminde tamamladı.
            var calls = InternalProbes.CompleteCalls(key);
            calls.Count.ShouldBe(1);
            (calls[0].InTransaction, calls[0].Result).ShouldBe((true, true));
            var record = await Record(key);
            (record.Status, JsonDocument.Parse(record.ResponseJson).RootElement.GetProperty("id").GetGuid()).ShouldBe((201, created.GetProperty("id").GetGuid()));

            InternalProbes.FailLateComplete = false;
            using var replay = await SendInternal(env.Tenant, body, key);
            replay.StatusCode.ShouldBe(HttpStatusCode.Created);
            (await replay.JsonAsync()).GetProperty("number").GetString().ShouldBe(created.GetProperty("number").GetString());
            (await StoredVouchers(env.Tenant)).Count.ShouldBe(1);
        } finally {
            InternalProbes.FailLateComplete = false;
        }
    }

    [Test]
    public async Task Internal_IdempotencyOwnershipLost_RollsBackEverything() {
        var env  = await NewEnv();
        var key  = "lost-" + Guid.NewGuid().ToString("N");
        var body = InternalBody([Line("770", 30m, 0m), Line("335", 0m, 30m, env.Supplier)], Guid.NewGuid());
        InternalProbes.LoseOwnership = true;
        try {
            using var response = await SendInternal(env.Tenant, body, key);

            // Kayıt devredildiği için işleyici işlemi geri alır: fiş, cari satırı ve olay yazılmaz; kayıt 201 olmaz.
            await Fail(response, HttpStatusCode.Conflict, "idempotency_in_progress");
            (await StoredVouchers(env.Tenant)).ShouldBeEmpty();
            (await Entries(env.Tenant)).ShouldBeEmpty();
            (await Outbox(env.Tenant, "journal_voucher.posted")).ShouldBeEmpty();
            await using var scope = TestEnvironment.Factory.Services.CreateAsyncScope();
            var             db    = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.IdempotencyRecords.AsNoTracking().Where(r => r.Key == key).Select(r => r.Status).ToListAsync()).ShouldNotContain(201);
        } finally {
            InternalProbes.LoseOwnership = false;
        }
    }

    [Test]
    public async Task Internal_FailedHandler_RollsBackVoucherAndReleasesKey() {
        var env  = await NewEnv();
        var key  = "unbalanced-" + Guid.NewGuid().ToString("N");
        var body = InternalBody([Line("770", 10m, 0m), Line("335", 0m, 9m, env.Supplier)], Guid.NewGuid());

        using var first = await SendInternal(env.Tenant, body, key);
        await Fail(first, HttpStatusCode.BadRequest, "voucher_unbalanced");

        (await StoredVouchers(env.Tenant)).ShouldBeEmpty();
        (await Entries(env.Tenant)).ShouldBeEmpty();
        // İstisnayla biten istekte kayıt serbest bırakılır (hata kaydedilmez): düzeltilen istek aynı anahtarla yeniden denenebilir.
        await using (var scope = TestEnvironment.Factory.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.IdempotencyRecords.AsNoTracking().AnyAsync(r => r.Key == key)).ShouldBeFalse();
        }

        var fixedBody = InternalBody([Line("770", 10m, 0m), Line("335", 0m, 10m, env.Supplier)], Guid.NewGuid());
        using var retry = await SendInternal(env.Tenant, fixedBody, key);
        retry.StatusCode.ShouldBe(HttpStatusCode.Created, await retry.Content.ReadAsStringAsync());
        (await StoredVouchers(env.Tenant)).Count.ShouldBe(1);
    }

    [Test]
    public async Task Internal_PeriodLocked_Returns409() {
        var env = await NewEnv();
        await LockPeriod(env.Client, "2026-02-28");
        var body = InternalBody([Line("770", 10m, 0m), Line("335", 0m, 10m, env.Supplier)], Guid.NewGuid(), new DateOnly(2026, 2, 20));

        using var response = await SendInternal(env.Tenant, body, "locked-" + Guid.NewGuid().ToString("N"));

        await Fail(response, HttpStatusCode.Conflict, "period_locked");
        (await StoredVouchers(env.Tenant)).ShouldBeEmpty();
        (await Entries(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task Internal_SameSourceTwiceWithDifferentKeys_Returns409_UntilCancelled() {
        var env    = await NewEnv();
        var source = Guid.NewGuid();
        var body   = InternalBody([Line("770", 10m, 0m), Line("100", 0m, 10m)], source);

        using var first = await SendInternal(env.Tenant, body, "src-a-" + Guid.NewGuid().ToString("N"));
        var       id    = (await first.JsonAsync()).GetProperty("id").GetGuid();
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var duplicate = await SendInternal(env.Tenant, body, "src-b-" + Guid.NewGuid().ToString("N"));
        await Fail(duplicate, HttpStatusCode.Conflict, "voucher_source_exists");
        (await StoredVouchers(env.Tenant)).Count.ShouldBe(1);

        using var cancel = await env.Client.PostAsJsonAsync($"{Vouchers}/{id}/cancel", new { reason = "Yeniden gönderilecek" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var again = await SendInternal(env.Tenant, body, "src-c-" + Guid.NewGuid().ToString("N"));
        again.StatusCode.ShouldBe(HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Internal_AuthRules_UnsignedWrongSecretWrongRouteAndInvalidBody() {
        var env  = await NewEnv();
        var body = InternalBody([Line("770", 10m, 0m), Line("100", 0m, 10m)], Guid.NewGuid());

        using (var wrongSecret = await SendInternal(env.Tenant, body, "k-" + Guid.NewGuid().ToString("N"), secret: "yanlis-sir-0123456789"))
            wrongSecret.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // İzin verilmeyen çağıran ("limited") bu yola erişemez.
        using (var limited = await SendInternal(env.Tenant, body, "k-" + Guid.NewGuid().ToString("N"), InternalProbes.LimitedCaller, InternalProbes.OtherSecret))
            limited.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // Oturumlu kullanıcı iç yola imzasız erişemez.
        using (var session = await env.Client.PostAsync(Internal, new StringContent(body, Encoding.UTF8, "application/json")))
            session.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var noSource = JsonSerializer.Serialize(new { date = Today.ToString("yyyy-MM-dd"), description = "x", lines = new[] { Line("770", 1m, 0m), Line("100", 0m, 1m) } });
        using (var invalid = await SendInternal(env.Tenant, noSource, "k-" + Guid.NewGuid().ToString("N")))
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredVouchers(env.Tenant)).ShouldBeEmpty();
    }
}
