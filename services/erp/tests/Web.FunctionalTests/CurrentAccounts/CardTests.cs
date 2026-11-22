using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.FunctionalTests.CurrentAccounts;

public class CardTests : TestBase
{
    private const string Url = "/api/erp/current-accounts";
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private sealed class FixedTenant(Guid? tenantId) : ICurrentTenant
    {
        public Guid? TenantId => tenantId;
    }

    private static Task<HttpClient> Manager(Guid? tenant = null) =>
        Factory.ClientAsync(tenant ?? Tenant, "cari.read", "cari.manage", "cari.movements");

    private static Dictionary<string, object?> Card(string code = "C0001", string name = "Örnek Ltd.", bool customer = true,
        bool supplier = false, string? taxNumber = null, string kind = "corporate", string currency = "TRY",
        object? address = null, object? opening = null) => new()
    {
        ["code"] = code,
        ["name"] = name,
        ["kind"] = kind,
        ["taxNumber"] = taxNumber,
        ["taxOffice"] = "Kadıköy",
        ["isEInvoiceTaxpayer"] = false,
        ["isCustomer"] = customer,
        ["isSupplier"] = supplier,
        ["phone"] = "0212 000 00 00",
        ["email"] = "info@ornek.com",
        ["notes"] = null,
        ["currency"] = currency,
        ["defaultDueDays"] = 30,
        ["creditLimit"] = null,
        ["address"] = address,
        ["openingBalance"] = opening,
    };

    private static async Task<Guid> CreateAsync(HttpClient client, object card)
    {
        using var response = await client.PostAsJsonAsync(Url, card);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"{Url}/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    private static string ValidVkn()
    {
        const string first9 = "123456789";
        for (var d = 0; d <= 9; d++)
        {
            if (ERP.Domain.CurrentAccounts.TaxNumber.IsValidVkn(first9 + d))
            {
                return first9 + d;
            }
        }

        throw new InvalidOperationException();
    }

    [Test]
    public async Task Create_Minimal_Returns201AndNextCodeAdvances()
    {
        using var client = await Manager();
        (await (await client.GetAsync($"{Url}/next-code")).JsonAsync()).GetProperty("code").GetString().ShouldBe("C0001");

        using var response = await client.PostAsJsonAsync(Url, Card());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.JsonAsync();
        body.GetProperty("code").GetString().ShouldBe("C0001");
        body.GetProperty("kind").GetString().ShouldBe("corporate");
        body.GetProperty("balance").GetDecimal().ShouldBe(0);
        body.GetProperty("hasMovements").GetBoolean().ShouldBeFalse();
        (await (await client.GetAsync($"{Url}/next-code")).JsonAsync()).GetProperty("code").GetString().ShouldBe("C0002");
    }

    [Test]
    public async Task Create_WithAddressAndOpening_ReturnsBalance()
    {
        using var client = await Manager();
        var id = await CreateAsync(client, Card(
            address: new { title = "Merkez", line = "Bağdat Cad. 1", city = "İstanbul", isBilling = true, isShipping = true },
            opening: new { amount = 1000m, direction = "debit", date = "2026-01-01" }));

        var body = await GetAsync(client, id);

        body.GetProperty("balance").GetDecimal().ShouldBe(1000m);
        body.GetProperty("hasMovements").GetBoolean().ShouldBeTrue();
        var address = body.GetProperty("addresses").EnumerateArray().Single();
        address.GetProperty("title").GetString().ShouldBe("Merkez");
        address.GetProperty("country").GetString().ShouldBe("Türkiye");
        address.GetProperty("isDefaultBilling").GetBoolean().ShouldBeTrue();
        address.GetProperty("isDefaultShipping").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task Create_CreditOpening_GivesNegativeBalance()
    {
        using var client = await Manager();
        var id = await CreateAsync(client, Card(opening: new { amount = 250.5m, direction = "credit", date = "2026-01-01" }));

        (await GetAsync(client, id)).GetProperty("balance").GetDecimal().ShouldBe(-250.5m);
    }

    [Test]
    public async Task Create_InvalidVkn_Returns400()
    {
        using var client = await Manager();

        using var response = await client.PostAsJsonAsync(Url, Card(taxNumber: "1234567891"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Create_ValidTckn_ForIndividual_Returns201()
    {
        using var client = await Manager();

        using var response = await client.PostAsJsonAsync(Url, Card(kind: "individual", taxNumber: "10000000146"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Create_NeitherCustomerNorSupplier_Returns400()
    {
        using var client = await Manager();

        using var response = await client.PostAsJsonAsync(Url, Card(customer: false, supplier: false));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Create_DuplicateCode_Returns409CodeTaken()
    {
        using var client = await Manager();
        await CreateAsync(client, Card());

        using var response = await client.PostAsJsonAsync(Url, Card(name: "Başka Ltd."));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("code_taken");
    }

    [Test]
    public async Task Create_DuplicateTaxNumber_Returns409()
    {
        using var client = await Manager();
        await CreateAsync(client, Card(taxNumber: ValidVkn()));

        using var response = await client.PostAsJsonAsync(Url, Card(code: "C0002", taxNumber: ValidVkn()));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("tax_number_taken");
    }

    [Test]
    public async Task List_FiltersByRoleSearchAndActive()
    {
        using var client = await Manager();
        await CreateAsync(client, Card(code: "C0001", name: "Müşteri A", customer: true, supplier: false));
        await CreateAsync(client, Card(code: "C0002", name: "Tedarikçi B", customer: false, supplier: true));
        await CreateAsync(client, Card(code: "C0003", name: "Her İkisi", customer: true, supplier: true, taxNumber: "10000000146", kind: "individual"));
        var inactive = await CreateAsync(client, Card(code: "C0004", name: "Pasif Cari", customer: true, supplier: true));
        (await client.PostAsync($"{Url}/{inactive}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        async Task<string[]> Codes(string query)
        {
            var body = await (await client.GetAsync($"{Url}{query}")).JsonAsync();
            return body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("code").GetString()!).ToArray();
        }

        (await Codes("")).ShouldBe(["C0001", "C0002", "C0003"]);
        (await Codes("?role=supplier")).ShouldBe(["C0002", "C0003"]);
        (await Codes("?role=customer")).ShouldBe(["C0001", "C0003"]);
        (await Codes("?search=tedarik")).ShouldBe(["C0002"]);
        (await Codes("?search=C0003")).ShouldBe(["C0003"]);
        (await Codes("?search=10000000146")).ShouldBe(["C0003"]);
        (await Codes("?includeInactive=true")).ShouldBe(["C0001", "C0002", "C0003", "C0004"]);
    }

    [Test]
    public async Task List_Search_FoldsTurkishDottedAndDotlessI()
    {
        using var client = await Manager();
        await CreateAsync(client, Card(code: "C0001", name: "YILMAZ TİCARET"));
        await CreateAsync(client, Card(code: "C0002", name: "Işık Ltd"));

        async Task<string[]> Codes(string term)
        {
            var body = await (await client.GetAsync($"{Url}?search={Uri.EscapeDataString(term)}")).JsonAsync();
            return body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("code").GetString()!).ToArray();
        }

        (await Codes("yılmaz")).ShouldBe(["C0001"]);
        (await Codes("Yılmaz")).ShouldBe(["C0001"]);
        (await Codes("ışık")).ShouldBe(["C0002"]);
        (await Codes("tic")).ShouldBe(["C0001"]);
    }

    [Test]
    public async Task Create_PostalCodeTooLong_Returns400()
    {
        using var client = await Manager();
        var address = new { title = "Merkez", line = "Adres", postalCode = new string('1', 21), isBilling = true, isShipping = false };

        using var response = await client.PostAsJsonAsync(Url, Card(address: address));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Create_CreditLimitWithThreeDecimals_Returns400()
    {
        using var client = await Manager();
        var card = Card();
        card["creditLimit"] = 10.123m;

        using var response = await client.PostAsJsonAsync(Url, card);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Create_UnknownKind_Returns400()
    {
        using var client = await Manager();
        var card = Card();
        card["kind"] = 5;

        using var response = await client.PostAsJsonAsync(Url, card);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AddEntry_DateBefore2000_Returns400()
    {
        using var client = await Manager();
        var id = await CreateAsync(client, Card());

        using var response = await client.PostAsJsonAsync($"{Url}/{id}/entries",
            new { type = "debit", date = "1899-12-31", amount = 10m });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Create_OpeningDateBefore2000_Returns400()
    {
        using var client = await Manager();

        using var response = await client.PostAsJsonAsync(Url,
            Card(opening: new { amount = 10m, direction = "debit", date = "1899-12-31" }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task List_PageTooLarge_Returns400()
    {
        using var client = await Manager();

        using var response = await client.GetAsync($"{Url}?page=200000");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task List_Paginates()
    {
        using var client = await Manager();
        for (var i = 1; i <= 30; i++)
        {
            await CreateAsync(client, Card(code: $"C{i:0000}", name: $"Cari {i}"));
        }

        var first = await (await client.GetAsync($"{Url}?pageSize=25")).JsonAsync();
        first.GetProperty("items").GetArrayLength().ShouldBe(25);
        first.GetProperty("total").GetInt32().ShouldBe(30);

        var second = await (await client.GetAsync($"{Url}?page=2&pageSize=25")).JsonAsync();
        second.GetProperty("items").GetArrayLength().ShouldBe(5);
        second.GetProperty("items")[0].GetProperty("code").GetString().ShouldBe("C0026");
    }

    [Test]
    public async Task List_ReportsBalanceOverLimitAndOverdue()
    {
        using var client = await Manager();
        var card = Card(opening: new { amount = 1000m, direction = "debit", date = "2020-01-01" });
        card["creditLimit"] = 500m;
        await CreateAsync(client, card);

        var item = (await (await client.GetAsync(Url)).JsonAsync()).GetProperty("items")[0];

        item.GetProperty("balance").GetDecimal().ShouldBe(1000m);
        item.GetProperty("overLimit").GetBoolean().ShouldBeTrue();
        item.GetProperty("overdueAmount").GetDecimal().ShouldBe(1000m);
    }

    [Test]
    public async Task Update_ChangesFields()
    {
        using var client = await Manager();
        var id = await CreateAsync(client, Card());

        using var response = await client.PutAsJsonAsync($"{Url}/{id}",
            Card(code: "C0009", name: "Yeni Ünvan", customer: false, supplier: true, currency: "USD"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await GetAsync(client, id);
        body.GetProperty("code").GetString().ShouldBe("C0009");
        body.GetProperty("name").GetString().ShouldBe("Yeni Ünvan");
        body.GetProperty("isSupplier").GetBoolean().ShouldBeTrue();
        body.GetProperty("currency").GetString().ShouldBe("USD");
    }

    [Test]
    public async Task Update_CodeOfAnotherCard_Returns409CodeTaken()
    {
        using var client = await Manager();
        await CreateAsync(client, Card(code: "C0001"));
        var second = await CreateAsync(client, Card(code: "C0002"));

        using var response = await client.PutAsJsonAsync($"{Url}/{second}", Card(code: "C0001"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("code_taken");
    }

    [Test]
    public async Task CurrencyLocked_EvenIfOnlyCancelledMovement()
    {
        using var client = await Manager();
        var id = await CreateAsync(client, Card(opening: new { amount = 100m, direction = "debit", date = "2026-01-01" }));

        var tenant = new FixedTenant(Tenant);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(TestEnvironment.ConnectionString)
            .AddInterceptors(new TenantInterceptor(tenant))
            .Options;
        await using (var db = new ApplicationDbContext(options, tenant))
        {
            var entry = await db.LedgerEntries.SingleAsync(e => e.CurrentAccountId == id);
            entry.CancelledAt = DateTimeOffset.UtcNow;
            entry.CancelReason = "Hatalı giriş";
            await db.SaveChangesAsync();
        }

        using var update = await client.PutAsJsonAsync($"{Url}/{id}", Card(currency: "EUR"));
        update.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await update.ErrorCodeAsync()).ShouldBe("currency_locked");

        using var delete = await client.DeleteAsync($"{Url}/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await delete.ErrorCodeAsync()).ShouldBe("has_movements");

        (await GetAsync(client, id)).GetProperty("balance").GetDecimal().ShouldBe(0);
    }

    [Test]
    public async Task Delete_WithoutMovements_Returns204()
    {
        using var client = await Manager();
        var id = await CreateAsync(client, Card(address: new { title = "Merkez", line = "Adres", isBilling = true, isShipping = false }));

        (await client.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Deactivate_ThenActivate()
    {
        using var client = await Manager();
        var id = await CreateAsync(client, Card());

        (await client.PostAsync($"{Url}/{id}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("isActive").GetBoolean().ShouldBeFalse();

        (await client.PostAsync($"{Url}/{id}/activate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task ReadOnly_CannotWrite_Returns403()
    {
        using var manager = await Manager();
        var id = await CreateAsync(manager, Card());
        using var reader = await Factory.ClientAsync(Tenant, "cari.read");

        (await reader.PostAsJsonAsync(Url, Card(code: "C0002"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PutAsJsonAsync($"{Url}/{id}", Card())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsync($"{Url}/{id}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"{Url}/next-code")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task OtherTenant_Get_Returns404()
    {
        using var owner = await Manager();
        var id = await CreateAsync(owner, Card());
        using var other = await Manager(Guid.CreateVersion7());

        (await other.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"{Url}/{id}", Card())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.PostAsync($"{Url}/{id}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await other.GetAsync(Url)).JsonAsync()).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task NoTenant_Returns403()
    {
        using var client = await Factory.ClientWithoutTenantAsync("cari.read", "cari.manage");

        (await client.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync(Url, Card())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
