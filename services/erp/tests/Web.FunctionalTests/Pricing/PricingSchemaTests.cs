using System.Text.Json;
using ERP.Domain.Pricing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Web.FunctionalTests.Pricing;

public class PricingSchemaTests : TestBase {
    private const           string Fiyat  = "/api/erp/settings/fiyat";
    private static readonly Guid   Tenant = Guid.CreateVersion7();

    private static Task<HttpClient> Admin(Guid? tenant = null) {
        return Factory.ClientAsync(tenant ?? Tenant, permissions: ["ayarlar.fiyat", "fiyat.read", "fiyat.manage"]);
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, int version, object value) {
        return client.PutAsJsonAsync(Fiyat, new { version, value });
    }

    [Test]
    public async Task Migration_CreatesPricingTables() {
        await using var connection = new NpgsqlConnection(TestEnvironment.ConnectionString);
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var command = new NpgsqlCommand(
                         "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", connection))
        await using (var reader = await command.ExecuteReaderAsync()) {
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }

        tables.ShouldContain("price_lists");
        tables.ShouldContain("price_list_items");
        tables.ShouldContain("customer_groups");
        tables.ShouldContain("customer_prices");
        tables.ShouldContain("price_history_entries");
        tables.ShouldContain("campaigns");

        await using var column = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'current_accounts' AND column_name = 'CustomerGroupId'",
            connection);
        ((long)(await column.ExecuteScalarAsync())!).ShouldBe(1);
    }

    [Test]
    public async Task Entities_RoundTrip_TiersCampaignArraysAndXminVersion() {
        var  productId = Guid.CreateVersion7();
        Guid listId;
        await using (var db = PricingTestSupport.Db(Tenant)) {
            db.Products.Add(new ERP.Domain.Catalog.Product { Id = productId, Code = "U00001", Name = "Tişört" });
            var list = new PriceList { Name = "Perakende", Kind = PriceListKind.Sale, ValidFrom = new DateOnly(2026, 1, 1) };
            listId = list.Id;
            db.PriceLists.Add(list);
            db.PriceListItems.Add(new PriceListItem {
                PriceListId = list.Id, ProductId = productId, Unit = "adet",
                Tiers = [new PriceTier(1, 100.1234m), new PriceTier(10, 90m)]
            });
            db.Campaigns.Add(new Campaign {
                Name = "Hafta sonu", Kind = CampaignKind.PercentOff, Percent = 10m,
                Days = [DayOfWeek.Saturday, DayOfWeek.Sunday], FromTime = new TimeOnly(9, 0), ToTime = new TimeOnly(18, 0),
                ProductIds = [productId], CategoryIds = [Guid.NewGuid(), Guid.NewGuid()]
            });
            await db.SaveChangesAsync();
        }

        await using (var db = PricingTestSupport.Db(Tenant)) {
            var item = await db.PriceListItems.SingleAsync();
            item.Tiers.ShouldBe([new PriceTier(1, 100.1234m), new PriceTier(10, 90m)]);
            item.Version.ShouldNotBe(0u);

            var list = await db.PriceLists.SingleAsync(l => l.Id == listId);
            var v1   = list.Version;
            v1.ShouldNotBe(0u);
            list.Name = "Perakende 2";
            await db.SaveChangesAsync();
            (await db.PriceLists.AsNoTracking().SingleAsync(l => l.Id == listId)).Version.ShouldNotBe(v1);

            var campaign = await db.Campaigns.SingleAsync();
            campaign.Days.ShouldBe([DayOfWeek.Saturday, DayOfWeek.Sunday]);
            campaign.FromTime.ShouldBe(new TimeOnly(9, 0));
            campaign.ProductIds.ShouldBe([productId]);
            campaign.CategoryIds.Length.ShouldBe(2);
            campaign.VariantIds.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task PricingSettings_DefaultsRoundTrip() {
        using var client = await Admin();

        var initial = await (await client.GetAsync(Fiyat)).JsonAsync();
        initial.GetProperty("version").GetInt32().ShouldBe(0);
        var value = initial.GetProperty("value");
        value.GetProperty("showVatIncluded").GetBoolean().ShouldBeTrue();
        value.GetProperty("rounding").GetString().ShouldBe("none");
        value.GetProperty("defaultSaleListId").ValueKind.ShouldBe(JsonValueKind.Null);

        using var put = await Put(client, 0, new { rounding = "nineEnding", showVatIncluded = false });
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await (await client.GetAsync(Fiyat)).JsonAsync();
        body.GetProperty("value").GetProperty("rounding").GetString().ShouldBe("nineEnding");
        body.GetProperty("value").GetProperty("showVatIncluded").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task Settings_DefaultSaleListMustBeSaleKind() {
        var sale     = await PricingTestSupport.AddListAsync(Tenant, PriceListKind.Sale, "Satış");
        var purchase = await PricingTestSupport.AddListAsync(Tenant, PriceListKind.Purchase, "Alış");
        using var client = await Admin();

        using var wrongSale = await Put(client, 0, new { defaultSaleListId = purchase });
        wrongSale.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await wrongSale.ErrorCodeAsync()).ShouldBe("default_list_required_kind");

        using var wrongPurchase = await Put(client, 0, new { defaultPurchaseListId = sale });
        wrongPurchase.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await wrongPurchase.ErrorCodeAsync()).ShouldBe("default_list_required_kind");

        using var missing = await Put(client, 0, new { defaultSaleListId = Guid.NewGuid() });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.ErrorCodeAsync()).ShouldBe("default_list_required_kind");

        using var ok = await Put(client, 0, new { defaultSaleListId = sale, defaultPurchaseListId = purchase });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await client.GetAsync(Fiyat)).JsonAsync()).GetProperty("value").GetProperty("defaultSaleListId")
            .GetGuid().ShouldBe(sale);
    }

    [Test]
    public async Task Settings_DefaultListOfOtherTenant_IsRejected() {
        var       foreign = await PricingTestSupport.AddListAsync(Guid.CreateVersion7(), PriceListKind.Sale);
        using var client  = await Admin();

        using var response = await Put(client, 0, new { defaultSaleListId = foreign });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).ShouldBe("default_list_required_kind");
    }

    [Test]
    public async Task Settings_RequiresAyarlarFiyat() {
        using var client = await Factory.ClientAsync(Tenant, permissions: ["fiyat.read", "fiyat.manage"]);

        (await client.GetAsync(Fiyat)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
