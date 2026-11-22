using System.Text.Json;
using ERP.Application.CurrentAccounts;

namespace ERP.Web.FunctionalTests.CurrentAccounts;

public class EntryTests : TestBase {
    private const           string Url    = "/api/erp/current-accounts";
    private static readonly Guid   Tenant = Guid.CreateVersion7();

    private static Task<HttpClient> Client(Guid? tenant = null) {
        return Factory.ClientAsync(tenant ?? Tenant, "cari.read", "cari.manage", "cari.movements");
    }

    private static async Task<Guid> CreateCardAsync(HttpClient client,
                                                    string     code        = "C0001",
                                                    decimal?   creditLimit = null,
                                                    int        dueDays     = 30,
                                                    object?    opening     = null
    ) {
        using var response = await client.PostAsJsonAsync(Url, new {
                                                                       code,
                                                                       name           = "Örnek Ltd.",
                                                                       kind           = "corporate",
                                                                       isCustomer     = true,
                                                                       currency       = "TRY",
                                                                       defaultDueDays = dueDays,
                                                                       creditLimit,
                                                                       openingBalance = opening
                                                                   });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostEntryAsync(HttpClient client, Guid id, object body) {
        return client.PostAsJsonAsync($"{Url}/{id}/entries", body);
    }

    private static async Task<JsonElement> AddAsync(HttpClient client, Guid id, object body) {
        using var response = await PostEntryAsync(client, id, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static object Debit(decimal amount, string date = "2026-11-01", string? dueDate = null) {
        return new { type = "debit", date, amount, dueDate };
    }

    private static object Credit(decimal amount, string date = "2026-11-01") {
        return new { type = "credit", date, amount };
    }

    private static object Opening(decimal amount, string direction = "debit", string date = "2026-01-01") {
        return new { type = "opening", date, amount, direction };
    }

    private static async Task<JsonElement> StatementAsync(HttpClient client, Guid id, string query = "") {
        using var response = await client.GetAsync($"{Url}/{id}/statement{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    [Test]
    public async Task AddDebit_DefaultsDueDateFromCard() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client, dueDays: 30);

        var result = await AddAsync(client, id, Debit(250.50m));

        var entry = result.GetProperty("entry");
        entry.GetProperty("dueDate").GetString().ShouldBe("2026-12-01");
        entry.GetProperty("type").GetString().ShouldBe("debitNote");
        entry.GetProperty("debit").GetDecimal().ShouldBe(250.50m);
        result.GetProperty("balance").GetDecimal().ShouldBe(250.50m);
        result.GetProperty("creditLimitExceeded").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task AddEntry_ToInactiveAccount_Returns409() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client);
        (await client.PostAsync($"{Url}/{id}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var response = await PostEntryAsync(client, id, Debit(10m));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("account_inactive");
    }

    [Test]
    public async Task SecondOpening_Returns409OpeningExists() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client);
        await AddAsync(client, id, Opening(100m));

        using var response = await PostEntryAsync(client, id, Opening(50m, "credit"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("opening_exists");
    }

    [Test]
    public async Task Opening_AfterCancel_CanBeReentered() {
        using var client  = await Client();
        var       id      = await CreateCardAsync(client);
        var       first   = await AddAsync(client, id, Opening(100m));
        var       entryId = first.GetProperty("entry").GetProperty("id").GetGuid();

        using var cancel
            = await client.PostAsJsonAsync($"{Url}/{id}/entries/{entryId}/cancel", new { reason = "Hatalı giriş" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var second = await AddAsync(client, id, Opening(70m, "credit"));
        second.GetProperty("balance").GetDecimal().ShouldBe(-70m);
    }

    [Test]
    public async Task CreditLimit_ExceededFlag() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client, creditLimit: 1000m);

        var first = await AddAsync(client, id, Debit(600m));
        first.GetProperty("creditLimitExceeded").GetBoolean().ShouldBeFalse();

        var second = await AddAsync(client, id, Debit(500m));
        second.GetProperty("creditLimitExceeded").GetBoolean().ShouldBeTrue();
        second.GetProperty("balance").GetDecimal().ShouldBe(1100m);
    }

    [Test]
    public async Task AddEntry_InvalidInput_Returns400() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client);

        foreach (var body in new[] {
                                       Debit(0m),
                                       Debit(-5m),
                                       Debit(10.123m),
                                       Debit(1_000_000_000_000m),
                                       new { type = "bogus", date   = "2026-11-01", amount = 10m },
                                       new { type = "opening", date = "2026-11-01", amount = 10m },
                                       new {
                                               type      = "opening", date = "2026-11-01", amount = 10m,
                                               direction = "sideways"
                                           },
                                       new {
                                               type       = "debit", date = "2026-11-01", amount = 10m,
                                               documentNo = new string('x', 51)
                                           },
                                       new {
                                               type        = "debit", date = "2026-11-01", amount = 10m,
                                               description = new string('x', 501)
                                           }
                                   }) {
            using var response = await PostEntryAsync(client, id, body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, JsonSerializer.Serialize(body));
        }
    }

    [Test]
    public async Task Cancel_RequiresReason_Returns400() {
        using var client  = await Client();
        var       id      = await CreateCardAsync(client);
        var       entryId = (await AddAsync(client, id, Debit(10m))).GetProperty("entry").GetProperty("id").GetGuid();

        foreach (var reason in new[] { "", "x", new string('x', 201) }) {
            using var response = await client.PostAsJsonAsync($"{Url}/{id}/entries/{entryId}/cancel", new { reason });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Test]
    public async Task Cancel_ExcludesFromBalance_AndStatementShowsStruck() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client);
        await AddAsync(client, id, Debit(100m, "2026-03-01"));
        var entryId = (await AddAsync(client, id, Debit(40m, "2026-03-02"))).GetProperty("entry").GetProperty("id")
            .GetGuid();

        using var cancel
            = await client.PostAsJsonAsync($"{Url}/{id}/entries/{entryId}/cancel", new { reason = "Yanlış tutar" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var card = await client.GetAsync($"{Url}/{id}");
        (await card.JsonAsync()).GetProperty("balance").GetDecimal().ShouldBe(100m);

        var statement = await StatementAsync(client, id, "?from=2026-01-01&to=2026-12-31");
        statement.GetProperty("totalDebit").GetDecimal().ShouldBe(100m);
        statement.GetProperty("closingBalance").GetDecimal().ShouldBe(100m);
        var rows = statement.GetProperty("rows").EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        var struck = rows.Single(r => r.GetProperty("id").GetGuid() == entryId);
        struck.GetProperty("cancelled").GetBoolean().ShouldBeTrue();
        struck.GetProperty("cancelReason").GetString().ShouldBe("Yanlış tutar");
    }

    [Test]
    public async Task Cancel_Twice_Returns409() {
        using var client  = await Client();
        var       id      = await CreateCardAsync(client);
        var       entryId = (await AddAsync(client, id, Debit(10m))).GetProperty("entry").GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"{Url}/{id}/entries/{entryId}/cancel", new { reason = "Hata" })).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        using var again = await client.PostAsJsonAsync($"{Url}/{id}/entries/{entryId}/cancel", new { reason = "Hata" });

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ErrorCodeAsync()).ShouldBe("already_cancelled");
    }

    [Test]
    public async Task CancelEntry_OfOtherAccount_Returns404() {
        using var client  = await Client();
        var       a       = await CreateCardAsync(client);
        var       b       = await CreateCardAsync(client, "C0002");
        var       entryId = (await AddAsync(client, a, Debit(10m))).GetProperty("entry").GetProperty("id").GetGuid();

        using var response
            = await client.PostAsJsonAsync($"{Url}/{b}/entries/{entryId}/cancel", new { reason = "Hata" });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var otherTenant = await Client(Guid.CreateVersion7());
        using var cross
            = await otherTenant.PostAsJsonAsync($"{Url}/{a}/entries/{entryId}/cancel", new { reason = "Hata" });
        cross.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Statement_DefaultRangeAndTotals() {
        using var client    = await Client();
        var       id        = await CreateCardAsync(client);
        var       today     = CurrentAccountClock.Today(TimeProvider.System);
        var       yearStart = new DateOnly(today.Year, 1, 1);
        var       lastYear  = yearStart.AddDays(-10).ToString("yyyy-MM-dd");
        var       inYear    = yearStart.ToString("yyyy-MM-dd");

        await AddAsync(client, id, Debit(300m, lastYear));
        await AddAsync(client, id, Debit(200m, inYear));
        await AddAsync(client, id, Credit(50m, today.ToString("yyyy-MM-dd")));

        var statement = await StatementAsync(client, id);

        statement.GetProperty("openingBalance").GetDecimal().ShouldBe(300m);
        statement.GetProperty("totalDebit").GetDecimal().ShouldBe(200m);
        statement.GetProperty("totalCredit").GetDecimal().ShouldBe(50m);
        statement.GetProperty("closingBalance").GetDecimal().ShouldBe(450m);
        statement.GetProperty("currency").GetString().ShouldBe("TRY");
        var rows = statement.GetProperty("rows").EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        rows[0].GetProperty("runningBalance").GetDecimal().ShouldBe(500m);
        rows[1].GetProperty("runningBalance").GetDecimal().ShouldBe(450m);

        using var bad = await client.GetAsync($"{Url}/{id}/statement?from=2026-12-31&to=2026-01-01");
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task MovementsPermission_Required() {
        using var manageOnly = await Factory.ClientAsync(Tenant, "cari.read", "cari.manage");
        var       id         = await CreateCardAsync(manageOnly);

        using var forbidden = await PostEntryAsync(manageOnly, id, Debit(10m));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var mover = await Factory.ClientAsync(Tenant, "cari.read", "cari.movements");
        using var ok    = await PostEntryAsync(mover, id, Debit(10m));
        ok.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task CreateCard_OpeningAmountWithThreeDecimals_Returns400() {
        using var client = await Client();
        using var response = await client.PostAsJsonAsync(Url, new {
                                                                       code           = "C0001",
                                                                       name           = "Örnek Ltd.",
                                                                       kind           = "corporate",
                                                                       isCustomer     = true,
                                                                       currency       = "TRY",
                                                                       defaultDueDays = 30,
                                                                       openingBalance
                                                                           = new {
                                                                                 amount = 10.123m, direction = "debit",
                                                                                 date   = "2026-01-01"
                                                                             }
                                                                   });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}