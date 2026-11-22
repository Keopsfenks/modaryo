using System.Text.Json;
using ERP.Application.CurrentAccounts;

namespace ERP.Web.FunctionalTests.CurrentAccounts;

public class DueTests : TestBase
{
    private const string Url = "/api/erp/current-accounts";

    private static Task<HttpClient> Client(Guid tenant) =>
        Factory.ClientAsync(tenant, "cari.read", "cari.manage", "cari.movements");

    private static string D(int offset) =>
        CurrentAccountClock.Today(TimeProvider.System).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<Guid> CreateCardAsync(HttpClient client, string code, string currency = "TRY")
    {
        using var response = await client.PostAsJsonAsync(Url, new
        {
            code,
            name = $"Cari {code}",
            kind = "corporate",
            isCustomer = true,
            isSupplier = true,
            currency,
            defaultDueDays = 30,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task AddAsync(HttpClient client, Guid id, object body)
    {
        using var response = await client.PostAsJsonAsync($"{Url}/{id}/entries", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static Task DebitAsync(HttpClient client, Guid id, decimal amount, string date, string dueDate) =>
        AddAsync(client, id, new { type = "debit", date, amount, dueDate });

    private static Task CreditAsync(HttpClient client, Guid id, decimal amount, string date) =>
        AddAsync(client, id, new { type = "credit", date, amount });

    private static async Task<JsonElement> SummaryAsync(HttpClient client)
    {
        using var response = await client.GetAsync($"{Url}/due-summary");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static JsonElement ForCurrency(JsonElement summary, string currency) =>
        summary.GetProperty("byCurrency").EnumerateArray().Single(c => c.GetProperty("currency").GetString() == currency);

    [Test]
    public async Task DueItems_MatchesDomainFifo()
    {
        using var client = await Client(Guid.CreateVersion7());
        var id = await CreateCardAsync(client, "C0001");
        await DebitAsync(client, id, 100m, "2026-01-01", "2026-01-10");
        await DebitAsync(client, id, 200m, "2026-02-01", "2026-02-10");
        await CreditAsync(client, id, 150m, "2026-02-15");

        using var response = await client.GetAsync($"{Url}/{id}/due-items");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = (await response.JsonAsync()).EnumerateArray().ToList();
        items.Count.ShouldBe(1);
        items[0].GetProperty("remaining").GetDecimal().ShouldBe(150m);
        items[0].GetProperty("dueDate").GetString().ShouldBe("2026-02-10");
        items[0].GetProperty("direction").GetString().ShouldBe("receivable");
        items[0].GetProperty("daysOverdue").GetInt32().ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task DueItems_OtherTenant_Returns404()
    {
        using var client = await Client(Guid.CreateVersion7());
        var id = await CreateCardAsync(client, "C0001");
        using var other = await Client(Guid.CreateVersion7());

        (await other.GetAsync($"{Url}/{id}/due-items")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DueSummary_SeparatesCurrencies()
    {
        using var client = await Client(Guid.CreateVersion7());
        var tryCard = await CreateCardAsync(client, "C0001", "TRY");
        var usdCard = await CreateCardAsync(client, "C0002", "USD");
        await DebitAsync(client, tryCard, 100m, D(-20), D(-5));
        await DebitAsync(client, usdCard, 50m, D(-20), D(-5));
        await CreditAsync(client, usdCard, 20m, D(5));
        await CreditAsync(client, tryCard, 7m, D(-40));

        var summary = await SummaryAsync(client);

        var tryBucket = ForCurrency(summary, "TRY");
        tryBucket.GetProperty("receivable").GetProperty("overdue").GetDecimal().ShouldBe(93m);
        tryBucket.GetProperty("payable").GetProperty("overdue").GetDecimal().ShouldBe(0m);
        var usdBucket = ForCurrency(summary, "USD");
        usdBucket.GetProperty("receivable").GetProperty("overdue").GetDecimal().ShouldBe(30m);
        usdBucket.GetProperty("payable").GetProperty("overdue").GetDecimal().ShouldBe(0m);
        summary.GetProperty("byCurrency").GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task DueSummary_BucketBoundaries()
    {
        using var client = await Client(Guid.CreateVersion7());
        var id = await CreateCardAsync(client, "C0001");
        await DebitAsync(client, id, 1m, D(-30), D(-1));
        await DebitAsync(client, id, 2m, D(-30), D(0));
        await DebitAsync(client, id, 4m, D(-30), D(30));
        await DebitAsync(client, id, 8m, D(-30), D(31));

        var receivable = ForCurrency(await SummaryAsync(client), "TRY").GetProperty("receivable");

        receivable.GetProperty("overdue").GetDecimal().ShouldBe(1m);
        receivable.GetProperty("within30").GetDecimal().ShouldBe(6m);
        receivable.GetProperty("later").GetDecimal().ShouldBe(8m);
    }

    [Test]
    public async Task DueSummary_TopOverdueOrdering()
    {
        using var client = await Client(Guid.CreateVersion7());
        var c1 = await CreateCardAsync(client, "C0001");
        var c2 = await CreateCardAsync(client, "C0002");
        var c3 = await CreateCardAsync(client, "C0003");
        var c4 = await CreateCardAsync(client, "C0004");
        await DebitAsync(client, c1, 100m, D(-30), D(-3));
        await DebitAsync(client, c2, 100m, D(-30), D(-10));
        await DebitAsync(client, c2, 50m, D(-30), D(-2));
        await DebitAsync(client, c3, 500m, D(-30), D(-7));
        await DebitAsync(client, c4, 900m, D(-30), D(5));
        (await client.PostAsync($"{Url}/{c2}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var top = (await SummaryAsync(client)).GetProperty("topOverdue").EnumerateArray().ToList();

        top.Select(t => t.GetProperty("code").GetString()).ToList().ShouldBe(["C0003", "C0002", "C0001"]);
        top[1].GetProperty("overdueReceivable").GetDecimal().ShouldBe(150m);
        top[1].GetProperty("maxDaysOverdue").GetInt32().ShouldBe(10);
        top[1].GetProperty("currency").GetString().ShouldBe("TRY");
        top[0].GetProperty("maxDaysOverdue").GetInt32().ShouldBe(7);
        top[2].GetProperty("id").GetGuid().ShouldBe(c1);
    }

    [Test]
    public async Task DueSummary_TopOverdue_TiesBrokenByCode()
    {
        using var client = await Client(Guid.CreateVersion7());
        var b = await CreateCardAsync(client, "C0002");
        var a = await CreateCardAsync(client, "C0001");
        await DebitAsync(client, b, 100m, D(-30), D(-3));
        await DebitAsync(client, a, 100m, D(-30), D(-3));

        var top = (await SummaryAsync(client)).GetProperty("topOverdue").EnumerateArray().ToList();

        top.Select(t => t.GetProperty("code").GetString()).ToList().ShouldBe(["C0001", "C0002"]);
    }

    [Test]
    public async Task DueSummary_OnlyOwnTenant()
    {
        using var client = await Client(Guid.CreateVersion7());
        var id = await CreateCardAsync(client, "C0001");
        await DebitAsync(client, id, 100m, D(-30), D(-3));
        using var other = await Client(Guid.CreateVersion7());

        var summary = await SummaryAsync(other);

        summary.GetProperty("byCurrency").GetArrayLength().ShouldBe(0);
        summary.GetProperty("topOverdue").GetArrayLength().ShouldBe(0);
    }
}
