using System.Net.Http.Json;
using ERP.Application.Pricing.Quotes;
using ERP.Domain.Pricing;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Text.Json;
using static ERP.Web.FunctionalTests.Pricing.PricingApi;

namespace ERP.Web.FunctionalTests.Pricing;

public class QuoteTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static object Money(decimal amount) {
        return new { amount, currency = "TRY", vatIncluded = true };
    }

    private static async Task<Guid> CreateCustomer(HttpClient client, string code, Guid? groupId = null) {
        using var response = await client.PostAsJsonAsync("/api/erp/current-accounts", new {
            code, name = "Müşteri " + code, kind = "corporate", isCustomer = true, isSupplier = false, currency = "TRY",
            defaultDueDays = 30, customerGroupId = groupId
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> Quote(HttpClient client, Guid product, Guid? customer = null, string unit = "adet",
                                                         string kind = "sale", string date = "2026-06-01", decimal qty = 1m) {
        var url = $"{Base}/quote?kind={kind}&productId={product}&unit={unit}&quantity={qty.ToString(System.Globalization.CultureInfo.InvariantCulture)}&date={date}"
                  + (customer is { } c ? $"&currentAccountId={c}" : "");
        return await client.GetAsync(url);
    }

    private static async Task<Guid> DefaultList(HttpClient client, decimal price, Guid product, object? extra = null) {
        var list = await CreateList(client, "Varsayılan", extra: extra);
        await PutRows(client, list, Row(product, Tiers((1, price))));
        await SetSettings(client, 0, new { defaultSaleListId = list });
        return list;
    }

    [Test]
    public async Task Quote_FractionalMeasureQuantity_UsesFirstTier() {
        using var client  = await Client(Tenant);
        var       product = await CreateProduct(client, "HALI", new { soldByMeasure = true, measureUnit = "m2", baseUnit = "m2" });
        var       list    = await CreateList(client, "Perakende");
        (await PutRows(client, list, Row(product, Tiers((1, 200m), (10, 180m)), unit: "m2"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await SetSettings(client, 0, new { defaultSaleListId = list });

        using var response = await Quote(client, product, unit: "m2", qty: 0.5m);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.JsonAsync()).GetProperty("unitPrice").GetDecimal().ShouldBe(200m);
    }

    [Test]
    public async Task Quote_CustomerSpecificWins() {
        using var client   = await Client(Tenant);
        var       product  = await CreateProduct(client, "P1", new { defaultSalePrice = Money(100m) });
        await DefaultList(client, 90m, product);
        var customer = await CreateCustomer(client, "C1");
        (await client.PostAsJsonAsync($"{Base}/customers/{customer}/prices", new {
            productId = product, unit = "adet", currency = "TRY", vatIncluded = true, tiers = Tiers((1, 80m))
        })).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var response = await Quote(client, product, customer);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.JsonAsync();
        body.GetProperty("unitPrice").GetDecimal().ShouldBe(80m);
        body.GetProperty("source").GetString().ShouldBe("customerPrice");
    }

    [Test]
    public async Task Quote_GroupListWhenNoCustomerPrice() {
        using var client  = await Client(Tenant);
        var       product = await CreateProduct(client, "P1", new { defaultSalePrice = Money(100m) });
        await DefaultList(client, 90m, product);
        var groupList = await CreateList(client, "Bayi");
        await PutRows(client, groupList, Row(product, Tiers((1, 85m))));
        using var group = await client.PostAsJsonAsync($"{Base}/customer-groups", new { name = "Bayiler", salePriceListId = groupList });
        var       customer = await CreateCustomer(client, "C1", (await group.JsonAsync()).GetProperty("id").GetGuid());

        var body = await (await Quote(client, product, customer)).JsonAsync();

        body.GetProperty("unitPrice").GetDecimal().ShouldBe(85m);
        body.GetProperty("source").GetString().ShouldBe("groupList");
        body.GetProperty("sourceId").GetGuid().ShouldBe(groupList);
    }

    [Test]
    public async Task Quote_DefaultListFallback() {
        using var client  = await Client(Tenant);
        var       product = await CreateProduct(client, "P1", new { defaultSalePrice = Money(100m) });
        var       list    = await DefaultList(client, 90m, product);
        var       customer = await CreateCustomer(client, "C1");

        var body = await (await Quote(client, product, customer)).JsonAsync();

        body.GetProperty("unitPrice").GetDecimal().ShouldBe(90m);
        body.GetProperty("source").GetString().ShouldBe("defaultList");
        body.GetProperty("sourceId").GetGuid().ShouldBe(list);
    }

    [Test]
    public async Task Quote_CardDefaultLastResort() {
        using var client  = await Client(Tenant);
        var       product = await CreateProduct(client, "P1", new { defaultSalePrice = Money(100m) });
        var       other   = await CreateProduct(client, "P2");
        await DefaultList(client, 50m, other);

        var body = await (await Quote(client, product)).JsonAsync();
        using var missing = await Quote(client, await CreateProduct(client, "P3"));

        body.GetProperty("unitPrice").GetDecimal().ShouldBe(100m);
        body.GetProperty("source").GetString().ShouldBe("cardDefault");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.ErrorCodeAsync()).ShouldBe("price_not_found");
    }

    [Test]
    public async Task Quote_KoliFromAdetPriceTimesFactor() {
        using var client = await Client(Tenant);
        var       product = await CreateProduct(client, "P1", new { units = new[] { new { unit = "koli", factor = 12m } } });
        var       list    = await DefaultList(client, 10m, product);

        var body = await (await Quote(client, product, unit: "koli")).JsonAsync();

        body.GetProperty("unitPrice").GetDecimal().ShouldBe(120m);
        body.GetProperty("source").GetString().ShouldBe("defaultList");
        body.GetProperty("sourceId").GetGuid().ShouldBe(list);
    }

    [Test]
    public async Task Quote_ExpiredListSkipped() {
        using var client  = await Client(Tenant);
        var       product = await CreateProduct(client, "P1", new { defaultSalePrice = Money(100m) });
        await DefaultList(client, 90m, product, new { validFrom = "2020-01-01", validTo = "2020-12-31" });

        var body = await (await Quote(client, product, date: "2026-06-01")).JsonAsync();
        var old  = await (await Quote(client, product, date: "2020-06-01")).JsonAsync();

        body.GetProperty("source").GetString().ShouldBe("cardDefault");
        old.GetProperty("unitPrice").GetDecimal().ShouldBe(90m);
    }

    [Test]
    public async Task QuoteMany_TwoRoundTrips() {
        using var client   = await Client(Tenant);
        var       p1       = await CreateProduct(client, "P1", new { defaultSalePrice = Money(100m) });
        var       p2       = await CreateProduct(client, "P2", new { units = new[] { new { unit = "koli", factor = 12m } } });
        var       p3       = await CreateProduct(client, "P3");
        var       list     = await CreateList(client, "Varsayılan");
        await PutRows(client, list, Row(p2, Tiers((1, 10m), (5, 9m))));
        await SetSettings(client, 0, new { defaultSaleListId = list });
        var customer = await CreateCustomer(client, "C1");

        var counter = new CommandCounter();
        var tenant  = new PricingTestSupport.FakeTenant(Tenant, 0m);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(TestEnvironment.ConnectionString)
            .AddInterceptors(new TenantInterceptor(tenant), counter).Options;
        await using var db      = new ApplicationDbContext(options, tenant);
        var             service = new PriceQuoteService(db);
        var date = new DateOnly(2026, 6, 1);

        var quotes = await service.QuoteManyAsync([
            new QuoteRequest(PriceListKind.Sale, customer, p1, null, "adet", 1m, date),
            new QuoteRequest(PriceListKind.Sale, customer, p2, null, "koli", 1m, date),
            new QuoteRequest(PriceListKind.Sale, null, p2, null, "adet", 5m, date),
            new QuoteRequest(PriceListKind.Sale, null, p3, null, "adet", 1m, date),
            new QuoteRequest(PriceListKind.Sale, null, Guid.CreateVersion7(), null, "adet", 1m, date)
        ], CancellationToken.None);

        counter.Count.ShouldBeLessThanOrEqualTo(2);
        quotes.Count.ShouldBe(5);
        quotes[0]!.UnitPrice.ShouldBe(100m);
        quotes[1]!.UnitPrice.ShouldBe(108m); // 1 koli = 12 adet: 5+ kademesi (9) x 12
        quotes[2]!.UnitPrice.ShouldBe(9m);
        quotes[3].ShouldBeNull();
        quotes[4].ShouldBeNull();
    }

    [Test]
    public async Task Quote_PurchaseWithoutAlis_Returns403() {
        using var admin   = await Client(Tenant);
        var       product = await CreateProduct(admin, "P1", new { defaultPurchasePrice = Money(40m) });

        using var reader = await Client(Tenant, "fiyat.read");
        using var denied = await Quote(reader, product, kind: "purchase");
        using var ok     = await Quote(admin, product, kind: "purchase");

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ok.JsonAsync()).GetProperty("unitPrice").GetDecimal().ShouldBe(40m);
    }

    private sealed class CommandCounter : DbCommandInterceptor {
        public int Count;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default) {
            Interlocked.Increment(ref Count);
            return base.ReaderExecutingAsync(command, eventData, result, ct);
        }
    }
}
