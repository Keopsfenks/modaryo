using System.Net.Http.Json;
using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Catalog;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Web.FunctionalTests.Catalog;

public class CatalogSchemaTests : TestBase {
    private const           string Katalog = "/api/erp/settings/katalog";
    private static readonly Guid   Tenant  = Guid.CreateVersion7();

    private static Task<HttpClient> Admin() {
        return Factory.ClientAsync(Tenant, permissions: ["ayarlar.katalog", "katalog.read", "katalog.manage"]);
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, int version, object value) {
        return client.PutAsJsonAsync(Katalog, new { version, value });
    }

    [Test]
    public async Task Migration_CreatesCatalogTables() {
        await using var connection = new NpgsqlConnection(TestEnvironment.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
                                                    "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'",
                                                    connection);
        var             tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));

        tables.ShouldContain("products");
        tables.ShouldContain("product_variants");
        tables.ShouldContain("product_units");
        tables.ShouldContain("product_suppliers");
        tables.ShouldContain("product_images");
        tables.ShouldContain("categories");
        tables.ShouldContain("brands");
        tables.ShouldContain("barcodes");
        tables.ShouldContain("custom_field_definitions");
    }

    [Test]
    public async Task CatalogSettings_DefaultsRoundTrip() {
        using var client = await Admin();

        var initial = await (await client.GetAsync(Katalog)).JsonAsync();
        initial.GetProperty("version").GetInt32().ShouldBe(0);
        var value = initial.GetProperty("value");
        value.GetProperty("codeTemplate").GetString().ShouldBe("U{00000}");
        value.GetProperty("defaultVatRate").GetInt32().ShouldBe(20);
        value.GetProperty("barcodePrefix").GetString().ShouldBe("2000000");
        value.GetProperty("nextBarcodeSequence").GetInt32().ShouldBe(1);
        value.GetProperty("nextCodeSequence").GetInt32().ShouldBe(1);
        value.GetProperty("units").EnumerateArray().Select(u => u.GetString()).ToArray()
            .ShouldBe(["adet", "kg", "g", "lt", "m", "m2", "m3", "koli", "paket", "saat", "gün", "iş"]);

        using var put = await Put(client, 0, new { defaultVatRate = 10, codeTemplate = "P{0000}" });
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await (await client.GetAsync(Katalog)).JsonAsync();
        body.GetProperty("version").GetInt32().ShouldBe(1);
        body.GetProperty("value").GetProperty("defaultVatRate").GetInt32().ShouldBe(10);
        body.GetProperty("value").GetProperty("codeTemplate").GetString().ShouldBe("P{0000}");
        body.GetProperty("value").GetProperty("units").GetArrayLength().ShouldBe(12);
    }

    [Test]
    public async Task SettingsValidation_InvalidVatRate_Returns400() {
        using var client = await Admin();

        (await Put(client, 0, new { defaultVatRate = 18 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(client, 0, new { barcodePrefix = "12345" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(client, 0, new { codeTemplate = "URUN" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(client, 0, new { units = Array.Empty<string>() })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(client, 0, new { defaultVatRate = 0 })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task SettingsPermission_RequiresAyarlarKatalog() {
        using var client = await Factory.ClientAsync(Tenant, permissions: ["katalog.read"]);

        (await client.GetAsync(Katalog)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Product_RoundTrips_WithOwnedMoneyJsonAndArrays() {
        var tenant = new FixedTenant(Tenant);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(TestEnvironment.ConnectionString)
            .AddInterceptors(new TenantInterceptor(tenant))
            .Options;
        var id = Guid.CreateVersion7();
        await using (var db = new ApplicationDbContext(options, tenant)) {
            var product = new Product {
                Id = id, Code = "U00001", Name = "Tişört", Tags = ["yaz", "pamuk"],
                DefaultSalePrice = new Money4(12.3456m, "TRY", true),
                Attributes = [new VariantAttribute { Name = "Beden", Values = ["S", "M"] }]
            };
            product.Variants.Add(new ProductVariant {
                ProductId = id, Code = "U00001-S", Values = new Dictionary<string, string> { ["Beden"] = "S" },
                SalePriceOverride = new Money4(15m, "USD", false)
            });
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options, tenant)) {
            var loaded = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == id);
            loaded.DefaultPurchasePrice.ShouldBeNull();
            loaded.DefaultSalePrice.ShouldBe(new Money4(12.3456m, "TRY", true));
            loaded.Tags.ShouldBe(["yaz", "pamuk"]);
            loaded.Attributes.Single().Values.ShouldBe(["S", "M"]);
            loaded.Variants.Single().Values["Beden"].ShouldBe("S");
            loaded.Variants.Single().SalePriceOverride.ShouldBe(new Money4(15m, "USD", false));
            loaded.Variants.Single().PurchasePriceOverride.ShouldBeNull();
        }
    }

    private sealed class FixedTenant(Guid tenantId) : ICurrentTenant {
        public Guid?                TenantId           => tenantId;
        public Guid?                UserId             => null;
        public string?              UserFullName       => "Test Kullanıcı";
        public IReadOnlyList<Guid>? BranchIds          => null;
        public decimal              MaxDiscountPercent => 0;

        public bool HasModule(string key) {
            return true;
        }

        public bool CanAccessBranch(Guid branchId) {
            return true;
        }
    }
}
