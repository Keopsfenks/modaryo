using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Web.FunctionalTests.Treasury.TreasuryTestSupport;

namespace ERP.Web.FunctionalTests.Treasury;

public class AccountTests : TestBase {
    private static readonly DateOnly Day = new(2026, 3, 10);

    [Test]
    public async Task CreateBankAccount_InvalidIban_Returns400() {
        var env = await NewEnv();

        using var bad = await env.Client.PostAsJsonAsync($"{Base}/accounts",
                                                         new { kind = "bank", name = "Garanti TL", currency = "TRY", branchId = env.Branch, iban = "TR330006100519786457841327", bankName = "Garanti" });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var missing = await env.Client.PostAsJsonAsync($"{Base}/accounts", new { kind = "bank", name = "Garanti TL", currency = "TRY", branchId = env.Branch, bankName = "Garanti" });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await CreateAccount(env.Client, env.Branch, "Garanti TL", "bank", "TRY", " tr33 0006 1005 1978 6457 8413 26 ", "Garanti");
        ok.GetProperty("iban").GetString().ShouldBe(Iban);
        ok.GetProperty("kind").GetString().ShouldBe("bank");
        ok.GetProperty("balance").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task CreateAccount_CashDropsBankFieldsAndRejectsBadInput() {
        var env = await NewEnv();

        var cash = await CreateAccount(env.Client, env.Branch, "Merkez Kasa", "cash", "TRY", Iban, "Garanti");
        cash.GetProperty("iban").ValueKind.ShouldBe(JsonValueKind.Null);
        cash.GetProperty("bankName").ValueKind.ShouldBe(JsonValueKind.Null);

        using var dupe = await env.Client.PostAsJsonAsync($"{Base}/accounts", new { kind = "cash", name = "Merkez Kasa", currency = "TRY", branchId = env.Branch });
        dupe.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await dupe.ErrorCodeAsync()).ShouldBe("name_taken");

        using var currency = await env.Client.PostAsJsonAsync($"{Base}/accounts", new { kind = "cash", name = "Altın", currency = "XAU", branchId = env.Branch });
        currency.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var branch = await env.Client.PostAsJsonAsync($"{Base}/accounts", new { kind = "cash", name = "Hayalet", currency = "TRY", branchId = Guid.NewGuid() });
        branch.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await branch.ErrorCodeAsync()).ShouldBe("branch_not_found");
    }

    [Test]
    public async Task AccountBalance_FromMovements() {
        var env = await NewEnv();
        var kasa = (await CreateAccount(env.Client, env.Branch, "Merkez Kasa")).GetProperty("id").GetGuid();
        var usd  = (await CreateAccount(env.Client, env.Branch, "USD Kasa", "cash", "USD")).GetProperty("id").GetGuid();
        await AddMovementAsync(env.Tenant, kasa, 1000m, Day);
        await AddMovementAsync(env.Tenant, kasa, -250.50m, Day.AddDays(1));
        await AddMovementAsync(env.Tenant, kasa, 100m, Day.AddDays(5));
        await AddMovementAsync(env.Tenant, usd, 40m, Day, "USD");

        var list = await (await env.Client.GetAsync($"{Base}/accounts")).JsonAsync();
        list.EnumerateArray().First(a => a.GetProperty("id").GetGuid() == kasa).GetProperty("balance").GetDecimal().ShouldBe(849.50m);
        list.EnumerateArray().First(a => a.GetProperty("id").GetGuid() == usd).GetProperty("balance").GetDecimal().ShouldBe(40m);

        var statement = await (await env.Client.GetAsync($"{Base}/accounts/{kasa}/movements?from={Day.AddDays(1):yyyy-MM-dd}")).JsonAsync();
        statement.GetProperty("openingBalance").GetDecimal().ShouldBe(1000m);
        statement.GetProperty("closingBalance").GetDecimal().ShouldBe(849.50m);
        var rows = statement.GetProperty("rows").EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        rows[0].GetProperty("balance").GetDecimal().ShouldBe(749.50m);
        rows[1].GetProperty("balance").GetDecimal().ShouldBe(849.50m);
        rows[0].GetProperty("documentNumber").GetString().ShouldStartWith("MF");
        rows[0].GetProperty("documentKind").GetString().ShouldBe("expense");

        var kindsRes = await env.Client.GetAsync($"{Base}/accounts?kind=Cash"); kindsRes.StatusCode.ShouldBe(HttpStatusCode.OK, await kindsRes.Content.ReadAsStringAsync()); var kinds = await kindsRes.JsonAsync();
        kinds.GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task AccountOtherBranch_Returns403() {
        var admin  = await NewEnv();
        var inB    = (await CreateAccount(admin.Client, admin.OtherBranch, "Kadıköy Kasa")).GetProperty("id").GetGuid();
        var inA    = (await CreateAccount(admin.Client, admin.Branch, "Merkez Kasa")).GetProperty("id").GetGuid();
        using var onlyA = await TestEnvironment.Factory.ClientAsync(admin.Tenant, null, [admin.Branch], null, Permissions);

        using var create = await onlyA.PostAsJsonAsync($"{Base}/accounts", new { kind = "cash", name = "X", currency = "TRY", branchId = admin.OtherBranch });
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await create.ErrorCodeAsync()).ShouldBe("branch_forbidden");

        (await onlyA.GetAsync($"{Base}/accounts/{inB}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await onlyA.GetAsync($"{Base}/accounts/{inB}/movements")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var update = await onlyA.PutAsJsonAsync($"{Base}/accounts/{inB}", new { kind = "cash", name = "Y", currency = "TRY", branchId = admin.OtherBranch });
        update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var visible = await (await onlyA.GetAsync($"{Base}/accounts")).JsonAsync();
        visible.EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ShouldBe([inA]);
    }

    [Test]
    public async Task AccountInUse_CannotBeDeletedOrChangeCurrency() {
        var env  = await NewEnv();
        var used = (await CreateAccount(env.Client, env.Branch, "Kullanılan")).GetProperty("id").GetGuid();
        var free = (await CreateAccount(env.Client, env.Branch, "Boş")).GetProperty("id").GetGuid();
        await AddMovementAsync(env.Tenant, used, 10m, Day);

        using var del = await env.Client.DeleteAsync($"{Base}/accounts/{used}");
        del.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await del.ErrorCodeAsync()).ShouldBe("account_in_use");

        using var currency = await env.Client.PutAsJsonAsync($"{Base}/accounts/{used}", new { kind = "cash", name = "Kullanılan", currency = "USD", branchId = env.Branch });
        currency.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await currency.ErrorCodeAsync()).ShouldBe("currency_locked");

        using var rename = await env.Client.PutAsJsonAsync($"{Base}/accounts/{used}", new { kind = "cash", name = "Yeni Ad", currency = "TRY", branchId = env.Branch });
        rename.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await env.Client.PostAsync($"{Base}/accounts/{free}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var list = await (await env.Client.GetAsync($"{Base}/accounts?includeInactive=false")).JsonAsync();
        list.EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ShouldBe([used]);
        (await env.Client.DeleteAsync($"{Base}/accounts/{free}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Accounts_RequirePermissions() {
        var env = await NewEnv();
        using var reader = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, ["finans.read"]);

        (await reader.GetAsync($"{Base}/accounts")).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var create = await reader.PostAsJsonAsync($"{Base}/accounts", new { kind = "cash", name = "K", currency = "TRY", branchId = env.Branch });
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var none = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, ["cari.read"]);
        (await none.GetAsync($"{Base}/accounts")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task BranchUsage_AccountMakesBranchInUse() {
        var env = await NewEnv();
        await CreateAccount(env.Client, env.Branch, "Merkez Kasa");

        var providers = TestEnvironment.Factory.Services.CreateScope().ServiceProvider.GetServices<ERP.Application.Common.Interfaces.IBranchUsageProvider>().ToList();
        var results = new List<bool>();
        foreach (var p in providers) results.Add(await p.IsInUseAsync(env.Tenant, env.Branch, CancellationToken.None));
        results.ShouldContain(true);
        var other = new List<bool>();
        foreach (var p in providers) other.Add(await p.IsInUseAsync(env.Tenant, env.OtherBranch, CancellationToken.None));
        other.ShouldAllBe(r => !r);
    }
}
