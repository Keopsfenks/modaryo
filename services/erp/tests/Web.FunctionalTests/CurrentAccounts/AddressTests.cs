using System.Text.Json;

namespace ERP.Web.FunctionalTests.CurrentAccounts;

public class AddressTests : TestBase
{
    private const string Url = "/api/erp/current-accounts";
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static Task<HttpClient> Manager(Guid? tenant = null) =>
        Factory.ClientAsync(tenant ?? Tenant, "cari.read", "cari.manage", "cari.movements");

    private static async Task<Guid> CreateCardAsync(HttpClient client, object? opening = null)
    {
        using var response = await client.PostAsJsonAsync(Url, new
        {
            code = "C0001",
            name = "Örnek Ltd.",
            kind = "corporate",
            isCustomer = true,
            currency = "TRY",
            defaultDueDays = 30,
            openingBalance = opening,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static object Address(string title = "Merkez", bool billing = false, bool shipping = false,
        bool defaultBilling = false, bool defaultShipping = false, string? country = null) => new
        {
            title,
            line = "Bağdat Cad. 1",
            city = "İstanbul",
            country,
            isBilling = billing,
            isShipping = shipping,
            isDefaultBilling = defaultBilling,
            isDefaultShipping = defaultShipping,
        };

    private static async Task<JsonElement> AddAsync(HttpClient client, Guid card, object address)
    {
        using var response = await client.PostAsJsonAsync($"{Url}/{card}/addresses", address);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static async Task<JsonElement[]> AddressesAsync(HttpClient client, Guid card)
    {
        var body = await (await client.GetAsync($"{Url}/{card}")).JsonAsync();
        return body.GetProperty("addresses").EnumerateArray().ToArray();
    }

    private static JsonElement ByTitle(JsonElement[] addresses, string title) =>
        addresses.Single(a => a.GetProperty("title").GetString() == title);

    [Test]
    public async Task FirstBillingAddress_BecomesDefault()
    {
        using var client = await Manager();
        var card = await CreateCardAsync(client);

        var first = await AddAsync(client, card, Address("A", billing: true));
        first.GetProperty("isDefaultBilling").GetBoolean().ShouldBeTrue();
        first.GetProperty("isDefaultShipping").GetBoolean().ShouldBeFalse();

        var second = await AddAsync(client, card, Address("B", billing: true));
        second.GetProperty("isDefaultBilling").GetBoolean().ShouldBeFalse();
        ByTitle(await AddressesAsync(client, card), "A").GetProperty("isDefaultBilling").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task NewDefault_ClearsPrevious()
    {
        using var client = await Manager();
        var card = await CreateCardAsync(client);
        await AddAsync(client, card, Address("A", billing: true, shipping: true));

        var second = await AddAsync(client, card, Address("B", billing: true, defaultBilling: true));

        second.GetProperty("isDefaultBilling").GetBoolean().ShouldBeTrue();
        var all = await AddressesAsync(client, card);
        ByTitle(all, "A").GetProperty("isDefaultBilling").GetBoolean().ShouldBeFalse();
        ByTitle(all, "A").GetProperty("isDefaultShipping").GetBoolean().ShouldBeTrue();
        ByTitle(all, "B").GetProperty("isDefaultShipping").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task Update_NewDefault_ClearsPrevious_AndLosingTypePromotesOldest()
    {
        using var client = await Manager();
        var card = await CreateCardAsync(client);
        var a = await AddAsync(client, card, Address("A", billing: true));
        await Task.Delay(5);
        var b = await AddAsync(client, card, Address("B", billing: true));
        await Task.Delay(5);
        await AddAsync(client, card, Address("C", billing: true));

        using var makeDefault = await client.PutAsJsonAsync($"{Url}/{card}/addresses/{b.GetProperty("id").GetGuid()}",
            Address("B", billing: true, defaultBilling: true));
        makeDefault.StatusCode.ShouldBe(HttpStatusCode.OK);
        var all = await AddressesAsync(client, card);
        ByTitle(all, "B").GetProperty("isDefaultBilling").GetBoolean().ShouldBeTrue();
        ByTitle(all, "A").GetProperty("isDefaultBilling").GetBoolean().ShouldBeFalse();

        using var shipOnly = await client.PutAsJsonAsync($"{Url}/{card}/addresses/{b.GetProperty("id").GetGuid()}",
            Address("B", shipping: true, defaultBilling: true));
        shipOnly.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await shipOnly.JsonAsync();
        body.GetProperty("isDefaultBilling").GetBoolean().ShouldBeFalse();
        body.GetProperty("isDefaultShipping").GetBoolean().ShouldBeTrue();
        all = await AddressesAsync(client, card);
        ByTitle(all, "A").GetProperty("isDefaultBilling").GetBoolean().ShouldBeTrue();
        a.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task Update_WithoutDefaultFlag_KeepsCurrentDefault()
    {
        using var client = await Manager();
        var card = await CreateCardAsync(client);
        await AddAsync(client, card, Address("A", billing: true));
        await Task.Delay(5);
        await AddAsync(client, card, Address("B", billing: true));
        await Task.Delay(5);
        var c = await AddAsync(client, card, Address("C", billing: true, defaultBilling: true));

        using var response = await client.PutAsJsonAsync($"{Url}/{card}/addresses/{c.GetProperty("id").GetGuid()}",
            Address("C yeni", billing: true, defaultBilling: false));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("isDefaultBilling").GetBoolean().ShouldBeTrue();
        var all = await AddressesAsync(client, card);
        ByTitle(all, "C yeni").GetProperty("isDefaultBilling").GetBoolean().ShouldBeTrue();
        ByTitle(all, "A").GetProperty("isDefaultBilling").GetBoolean().ShouldBeFalse();
        ByTitle(all, "B").GetProperty("isDefaultBilling").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task DeletingDefault_PromotesOldest()
    {
        using var client = await Manager();
        var card = await CreateCardAsync(client);
        var first = await AddAsync(client, card, Address("A", billing: true));
        await Task.Delay(5);
        await AddAsync(client, card, Address("B", billing: true));
        await Task.Delay(5);
        await AddAsync(client, card, Address("C", billing: true));

        using var delete = await client.DeleteAsync($"{Url}/{card}/addresses/{first.GetProperty("id").GetGuid()}");

        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var all = await AddressesAsync(client, card);
        all.Length.ShouldBe(2);
        ByTitle(all, "B").GetProperty("isDefaultBilling").GetBoolean().ShouldBeTrue();
        ByTitle(all, "C").GetProperty("isDefaultBilling").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task Address_NeitherBillingNorShipping_Returns400()
    {
        using var client = await Manager();
        var card = await CreateCardAsync(client);

        using var response = await client.PostAsJsonAsync($"{Url}/{card}/addresses", Address("A"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Address_OfOtherTenant_Returns404()
    {
        using var owner = await Manager();
        var ownerCard = await CreateCardAsync(owner);
        var ownerAddress = await AddAsync(owner, ownerCard, Address("A", billing: true));
        var ownerAddressId = ownerAddress.GetProperty("id").GetGuid();
        using var other = await Manager(Guid.CreateVersion7());
        var otherCard = await CreateCardAsync(other);

        (await other.PostAsJsonAsync($"{Url}/{ownerCard}/addresses", Address("X", billing: true))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"{Url}/{ownerCard}/addresses/{ownerAddressId}", Address("X", billing: true))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"{Url}/{otherCard}/addresses/{ownerAddressId}", Address("X", billing: true))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"{Url}/{otherCard}/addresses/{ownerAddressId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AddressesAsync(owner, ownerCard)).Length.ShouldBe(1);
    }

    [Test]
    public async Task Address_OfAnotherCardInSameTenant_Returns404()
    {
        using var client = await Manager();
        var cardA = await CreateCardAsync(client);
        var address = await AddAsync(client, cardA, Address("A", billing: true));
        using var second = await client.PostAsJsonAsync(Url, new
        {
            code = "C0002",
            name = "Diğer Ltd.",
            kind = "corporate",
            isCustomer = true,
            currency = "TRY",
            defaultDueDays = 30,
        });
        var cardB = (await second.JsonAsync()).GetProperty("id").GetGuid();

        (await client.DeleteAsync($"{Url}/{cardB}/addresses/{address.GetProperty("id").GetGuid()}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"{Url}/{cardB}/addresses/{address.GetProperty("id").GetGuid()}", Address("A", billing: true)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Address_CountryDefaultsToTurkiye()
    {
        using var client = await Manager();
        var card = await CreateCardAsync(client);

        var address = await AddAsync(client, card, Address("A", shipping: true));

        address.GetProperty("country").GetString().ShouldBe("Türkiye");
    }

    [Test]
    public async Task Address_ReadOnlyUser_Returns403()
    {
        using var manager = await Manager();
        var card = await CreateCardAsync(manager);
        using var reader = await Factory.ClientAsync(Tenant, "cari.read");

        (await reader.PostAsJsonAsync($"{Url}/{card}/addresses", Address("A", billing: true))).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Opening_Credit_ShowsAsCreditBalance()
    {
        using var client = await Manager();
        var card = await CreateCardAsync(client, new { amount = 500m, direction = "credit", date = "2026-01-01" });

        var body = await (await client.GetAsync($"{Url}/{card}")).JsonAsync();

        body.GetProperty("balance").GetDecimal().ShouldBe(-500m);
    }
}
