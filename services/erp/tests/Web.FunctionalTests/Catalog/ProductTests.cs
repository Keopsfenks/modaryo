using System.Net.Http.Json;
using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Catalog;
using ERP.Domain.Files;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;

namespace ERP.Web.FunctionalTests.Catalog;

public class ProductTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static object Product(string? code = null, string name = "Tişört", string kind = "product", object? extra = null) {
        var body = new Dictionary<string, object?> { ["kind"] = kind, ["name"] = name, ["code"] = code };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);

        return body;
    }

    [Test]
    public async Task CreateProduct_Minimal_Returns201AndNextCodeAdvances() {
        using var client = await Manager(Tenant);
        (await (await client.GetAsync($"{Products}/next-code")).JsonAsync()).GetProperty("code").GetString()
            .ShouldBe("U00001");

        var created = await CreateOk(client, Product("U00001"));

        created.GetProperty("code").GetString().ShouldBe("U00001");
        created.GetProperty("kind").GetString().ShouldBe("product");
        created.GetProperty("baseUnit").GetString().ShouldBe("adet");
        created.GetProperty("vatRate").GetInt32().ShouldBe(20);
        created.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        created.GetProperty("inUse").GetBoolean().ShouldBeFalse();
        (await (await client.GetAsync($"{Products}/next-code")).JsonAsync()).GetProperty("code").GetString()
            .ShouldBe("U00002");

        // Kod boş bırakılırsa şablondan verilir ve sayaç ilerler.
        var auto = await CreateOk(client, Product(null, "Pantolon"));
        auto.GetProperty("code").GetString().ShouldBe("U00002");
        (await (await client.GetAsync("/api/erp/settings/katalog")).JsonAsync()).GetProperty("value")
            .GetProperty("nextCodeSequence").GetInt32().ShouldBe(3);
    }

    [Test]
    public async Task CreateService_WithVariants_Returns400() {
        using var client = await Manager(Tenant);

        using var response = await client.PostAsJsonAsync(Products, Product("H1", "Montaj", "service", new {
            attributes = new[] { new { name = "Renk", values = new[] { "Kırmızı" } } }
        }));
        using var measure = await client.PostAsJsonAsync(Products, Product("H2", "Kesim", "service", new {
            soldByMeasure = true, measureUnit = "m2", baseUnit = "m2"
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        measure.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var service = await CreateOk(client, Product("H3", "Montaj", "service", new { baseUnit = "saat" }));
        service.GetProperty("kind").GetString().ShouldBe("service");
        service.GetProperty("baseUnit").GetString().ShouldBe("saat");
    }

    [Test]
    public async Task MeasureProductWithAdetUnit_Returns400() {
        using var client = await Manager(Tenant);

        using var response = await client.PostAsJsonAsync(Products, Product("K1", "Kumaş", extra: new {
            soldByMeasure = true, measureUnit = "m2", baseUnit = "adet"
        }));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await CreateOk(client, Product("K2", "Kumaş", extra: new {
            soldByMeasure = true, measureUnit = "m2", baseUnit = "m2"
        }));
        ok.GetProperty("soldByMeasure").GetBoolean().ShouldBeTrue();
        ok.GetProperty("measureUnit").GetString().ShouldBe("m2");

        // Kullanımdaki ürünün ölçüyle satışı kapatılamaz.
        var id = ok.GetProperty("id").GetGuid();
        Factory.ItemUsage.MarkProductUsed(id);
        using var off = await client.PutAsJsonAsync($"{Products}/{id}", Product("K2", "Kumaş", extra: new { baseUnit = "m2" }));
        off.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task DuplicateCode_Returns409() {
        using var client = await Manager(Tenant);
        var first = await CreateOk(client, Product("DUP1", extra: new {
            attributes = new[] { new { name = "Beden", values = new[] { "S", "M" } } }
        }));
        first.GetProperty("variants").GetArrayLength().ShouldBe(2);
        first.GetProperty("variants")[0].GetProperty("code").GetString().ShouldBe("DUP1-1");

        using var duplicate = await client.PostAsJsonAsync(Products, Product("DUP1"));
        using var clashWithVariant = await client.PostAsJsonAsync(Products, Product("DUP1-2"));

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ErrorCodeAsync()).ShouldBe("code_taken");
        clashWithVariant.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await clashWithVariant.ErrorCodeAsync()).ShouldBe("code_taken");
    }

    [Test]
    public async Task UnitFactorZero_Returns400InvalidConversion() {
        using var client = await Manager(Tenant);

        using var zero = await client.PostAsJsonAsync(Products, Product("B1", extra: new {
            units = new[] { new { unit = "koli", factor = 0m, barcode = (string?)null } }
        }));
        using var many = await client.PostAsJsonAsync(Products, Product("B2", extra: new {
            units = new[] { new { unit = "koli", factor = 1.1234567m, barcode = (string?)null } }
        }));

        zero.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await zero.ErrorCodeAsync()).ShouldBe("invalid_conversion");
        many.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await CreateOk(client, Product("B3", extra: new {
            units = new[] { new { unit = "koli", factor = 12m, barcode = (string?)null } }
        }));
        ok.GetProperty("units")[0].GetProperty("factor").GetDecimal().ShouldBe(12m);
    }

    [Test]
    public async Task SupplierNotSupplierRole_Returns400() {
        using var client   = await Manager(Tenant);
        var       customer = await CreateSupplier(client, "C1", false);
        var       supplier = await CreateSupplier(client, "C2", true);

        using var bad = await client.PostAsJsonAsync(Products, Product("S1", extra: new {
            suppliers = new[] { new { currentAccountId = customer, supplierCode = "X", isDefault = true } }
        }));
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await CreateOk(client, Product("S2", extra: new {
            suppliers = new[] { new { currentAccountId = supplier, supplierCode = "TDR-9", isDefault = true } }
        }));
        var link = ok.GetProperty("suppliers")[0];
        link.GetProperty("supplierCode").GetString().ShouldBe("TDR-9");
        link.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task TracksSerialLotWithoutModule_Returns403() {
        using var without = await Manager(Tenant, ["pos"]);
        using var with    = await Manager(Tenant, ["pos", "seri-lot"]);

        using var denied = await without.PostAsJsonAsync(Products, Product("L1", extra: new { tracksSerialLot = true }));
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.ErrorCodeAsync()).ShouldBe("module_disabled");

        var ok = await CreateOk(with, Product("L2", extra: new { tracksSerialLot = true }));
        ok.GetProperty("tracksSerialLot").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task DeleteInUse_Returns409() {
        using var client = await Manager(Tenant);
        var       used   = (await CreateOk(client, Product("D1"))).GetProperty("id").GetGuid();
        var       free   = (await CreateOk(client, Product("D2"))).GetProperty("id").GetGuid();
        Factory.ItemUsage.MarkProductUsed(used);

        using var blocked = await client.DeleteAsync($"{Products}/{used}");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.ErrorCodeAsync()).ShouldBe("product_in_use");
        (await (await client.GetAsync($"{Products}/{used}")).JsonAsync()).GetProperty("inUse").GetBoolean().ShouldBeTrue();

        (await client.DeleteAsync($"{Products}/{free}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync($"{Products}/{free}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await client.PostAsync($"{Products}/{used}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await (await client.GetAsync($"{Products}/{used}")).JsonAsync()).GetProperty("isActive").GetBoolean().ShouldBeFalse();
        (await client.PostAsync($"{Products}/{used}/activate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task List_FiltersByKindCategoryTag() {
        using var client = await Manager(Tenant);
        var       parent = (await (await client.PostAsJsonAsync("/api/erp/catalog/categories", new { name = "Giyim" })).JsonAsync())
            .GetProperty("id").GetGuid();
        var child = (await (await client.PostAsJsonAsync("/api/erp/catalog/categories", new { name = "Erkek", parentId = parent })).JsonAsync())
            .GetProperty("id").GetGuid();
        var other = (await (await client.PostAsJsonAsync("/api/erp/catalog/categories", new { name = "Ev" })).JsonAsync())
            .GetProperty("id").GetGuid();

        await CreateOk(client, Product("P1", "Işık Lamba", extra: new { categoryId = child, tags = new[] { "yaz" } }));
        await CreateOk(client, Product("P2", "Masa", extra: new { categoryId = other, tags = new[] { "kış" } }));
        await CreateOk(client, Product("H1", "Montaj", "service"));
        var inactive = (await CreateOk(client, Product("P3", "Eski"))).GetProperty("id").GetGuid();
        await client.PostAsync($"{Products}/{inactive}/deactivate", null);

        async Task<string[]> Codes(string query) {
            var resp = await client.GetAsync($"{Products}?{query}"); resp.StatusCode.ShouldBe(HttpStatusCode.OK, query + await resp.Content.ReadAsStringAsync()); var body = await resp.JsonAsync();
            return body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("code").GetString()!).ToArray();
        }

        (await Codes("")).ShouldBe(["H1", "P1", "P2"]);
        (await Codes("kind=service")).ShouldBe(["H1"]);
        (await Codes($"categoryId={parent}")).ShouldBe(["P1"]);
        (await Codes($"categoryId={other}")).ShouldBe(["P2"]);
        (await Codes("tag=yaz")).ShouldBe(["P1"]);
        (await Codes("includeInactive=true")).ShouldBe(["H1", "P1", "P2", "P3"]);
        (await Codes("search=p2")).ShouldBe(["P2"]);

        var list = await (await client.GetAsync($"{Products}?categoryId={child}")).JsonAsync();
        list.GetProperty("total").GetInt32().ShouldBe(1);
        list.GetProperty("items")[0].GetProperty("categoryPath").GetString().ShouldBe("Giyim > Erkek");
    }

    [Test]
    public async Task Update_ReplacesUnitsSuppliersAndImages() {
        using var client = await Manager(Tenant);
        var       fileId = await InsertImageAsync();
        var created = await CreateOk(client, Product("U1", extra: new {
            units = new[] { new { unit = "koli", factor = 12m, barcode = "8690000000001" } },
            images = new[] { new { fileId } }
        }));
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("images").GetArrayLength().ShouldBe(1);

        using var update = await client.PutAsJsonAsync($"{Products}/{id}", Product("U1-B", "Yeni ad", extra: new {
            units = new[] { new { unit = "koli", factor = 24m, barcode = (string?)"8690000000001" }, new { unit = "paket", factor = 6m, barcode = (string?)null } },
            tags = new[] { "a", "b" }, defaultSalePrice = new { amount = 12.5m, currency = "TRY", vatIncluded = true }
        }));

        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var body = await update.JsonAsync();
        body.GetProperty("code").GetString().ShouldBe("U1-B");
        body.GetProperty("units").GetArrayLength().ShouldBe(2);
        body.GetProperty("images").GetArrayLength().ShouldBe(0);
        body.GetProperty("defaultSalePrice").GetProperty("amount").GetDecimal().ShouldBe(12.5m);

        using var notFound = await client.PutAsJsonAsync($"{Products}/{id}", Product("U1-B", extra: new { images = new[] { new { fileId = Guid.CreateVersion7() } } }));
        notFound.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task OtherTenant_Returns404() {
        using var owner = await Manager(Tenant);
        var       id    = (await CreateOk(owner, Product("O1"))).GetProperty("id").GetGuid();

        using var other = await Manager(Guid.CreateVersion7());

        (await other.GetAsync($"{Products}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"{Products}/{id}", Product("O1"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"{Products}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.PostAsync($"{Products}/{id}/barcode/generate", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await other.GetAsync(Products)).JsonAsync()).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task ReadOnly_CannotWrite_403() {
        using var read = await Factory.ClientAsync(Tenant, "katalog.read");

        (await read.GetAsync(Products)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await read.PostAsJsonAsync(Products, Product("R1"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await read.GetAsync($"{Products}/next-code")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> InsertImageAsync() {
        var tenant = new FixedTenant(Tenant);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(TestEnvironment.ConnectionString).AddInterceptors(new TenantInterceptor(tenant)).Options;
        await using var db = new ApplicationDbContext(options, tenant);
        var file = new StoredFile {
            Kind = "image", FileName = "a.png", ContentType = "image/png", Size = 1, ObjectKey = "k/" + Guid.NewGuid(),
            Sha256 = new string('a', 64), CreatedAt = DateTimeOffset.UtcNow
        };
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
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
