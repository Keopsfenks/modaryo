using System.Net.Http.Json;
using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Catalog;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.FunctionalTests.Catalog;

public class CategoryBrandTests : TestBase {
    private const           string Categories = "/api/erp/catalog/categories";
    private const           string Brands     = "/api/erp/catalog/brands";
    private static readonly Guid   Tenant     = Guid.CreateVersion7();

    private static Task<HttpClient> Manager(Guid? tenant = null) {
        return Factory.ClientAsync(tenant ?? Tenant, "katalog.read", "katalog.manage");
    }

    private static async Task<Guid> CreateCategory(HttpClient client, string name, Guid? parent = null) {
        using var response = await client.PostAsJsonAsync(Categories, new { name, parentId = parent });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    [Test]
    public async Task CreateNestedCategories_TreeReturned() {
        using var client = await Manager();
        var       giyim  = await CreateCategory(client, "Giyim");
        var       erkek  = await CreateCategory(client, "Erkek", giyim);
        await CreateCategory(client, "Tişört", erkek);
        await CreateCategory(client, "Elektronik");

        var tree = await (await client.GetAsync(Categories)).JsonAsync();

        tree.GetArrayLength().ShouldBe(2);
        var root = tree.EnumerateArray().Single(n => n.GetProperty("name").GetString() == "Giyim");
        var mid  = root.GetProperty("children").EnumerateArray().Single();
        mid.GetProperty("name").GetString().ShouldBe("Erkek");
        mid.GetProperty("parentId").GetGuid().ShouldBe(giyim);
        mid.GetProperty("children").EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("Tişört");
    }

    [Test]
    public async Task MoveUnderOwnDescendant_Returns400() {
        using var client = await Manager();
        var       a      = await CreateCategory(client, "A");
        var       b      = await CreateCategory(client, "B", a);
        var       c      = await CreateCategory(client, "C", b);

        using var response = await client.PutAsJsonAsync($"{Categories}/{a}", new { name = "A", parentId = c, sort = 0 });
        using var self     = await client.PutAsJsonAsync($"{Categories}/{a}", new { name = "A", parentId = a, sort = 0 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        self.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var ok = await client.PutAsJsonAsync($"{Categories}/{c}", new { name = "C2", parentId = (Guid?)null, sort = 3 });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ok.JsonAsync()).GetProperty("name").GetString().ShouldBe("C2");
    }

    [Test]
    public async Task DeleteWithChildren_Returns409() {
        using var client = await Manager();
        var       parent = await CreateCategory(client, "Üst");
        var       child  = await CreateCategory(client, "Alt", parent);

        using var blocked = await client.DeleteAsync($"{Categories}/{parent}");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.ErrorCodeAsync()).ShouldBe("category_has_children");

        (await client.DeleteAsync($"{Categories}/{child}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"{Categories}/{parent}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task DeleteUsedCategory_Returns409() {
        using var client   = await Manager();
        var       category = await CreateCategory(client, "Kullanılan");
        await InsertProductAsync(categoryId: category);

        using var response = await client.DeleteAsync($"{Categories}/{category}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("category_in_use");
        var tree = await (await client.GetAsync(Categories)).JsonAsync();
        tree[0].GetProperty("productCount").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task DepthOver5_Returns400() {
        using var client = await Manager();
        Guid?     parent = null;
        for (var level = 1; level <= 5; level++) parent = await CreateCategory(client, $"Seviye {level}", parent);

        using var response = await client.PostAsJsonAsync(Categories, new { name = "Seviye 6", parentId = parent });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).ShouldBe("category_too_deep");

        // Alt ağacı olan bir kategoriyi taşıyıp 5 seviyeyi aşmak da yasak.
        var other = await CreateCategory(client, "Başka");
        var child = await CreateCategory(client, "Başka alt", other);
        using var move = await client.PutAsJsonAsync($"{Categories}/{other}", new { name = "Başka", parentId = parent, sort = 0 });
        move.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        child.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task Brand_DuplicateName_Returns409() {
        using var client = await Manager();
        (await client.PostAsJsonAsync(Brands, new { name = "Nike" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var duplicate = await client.PostAsJsonAsync(Brands, new { name = "Nike" });

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ErrorCodeAsync()).ShouldBe("name_taken");
    }

    [Test]
    public async Task Brand_UpdateAndDeleteInUse() {
        using var client = await Manager();
        var       id     = (await (await client.PostAsJsonAsync(Brands, new { name = "Adidas" })).JsonAsync()).GetProperty("id").GetGuid();

        using var renamed = await client.PutAsJsonAsync($"{Brands}/{id}", new { name = "Adidas TR" });
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await renamed.JsonAsync()).GetProperty("name").GetString().ShouldBe("Adidas TR");

        await InsertProductAsync(brandId: id);
        using var blocked = await client.DeleteAsync($"{Brands}/{id}");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.ErrorCodeAsync()).ShouldBe("brand_in_use");
    }

    [Test]
    public async Task OtherTenant_Returns404() {
        using var owner = await Manager();
        var       cat   = await CreateCategory(owner, "Gizli");
        var       brand = (await (await owner.PostAsJsonAsync(Brands, new { name = "Gizli marka" })).JsonAsync()).GetProperty("id").GetGuid();

        using var other = await Manager(Guid.CreateVersion7());

        (await other.PutAsJsonAsync($"{Categories}/{cat}", new { name = "X", parentId = (Guid?)null, sort = 0 })).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"{Categories}/{cat}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"{Brands}/{brand}", new { name = "X" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"{Brands}/{brand}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync(Categories).JsonBody()).GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task ReadOnly_CannotWrite_403() {
        using var admin = await Manager();
        var       cat   = await CreateCategory(admin, "Okunur");
        using var read  = await Factory.ClientAsync(Tenant, "katalog.read");

        (await read.GetAsync(Categories)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await read.GetAsync(Brands)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await read.PostAsJsonAsync(Categories, new { name = "Y" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await read.DeleteAsync($"{Categories}/{cat}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await read.PostAsJsonAsync(Brands, new { name = "Y" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task InsertProductAsync(Guid? categoryId = null, Guid? brandId = null) {
        var tenant = new FixedTenant(Tenant);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(TestEnvironment.ConnectionString)
            .AddInterceptors(new TenantInterceptor(tenant))
            .Options;
        await using var db = new ApplicationDbContext(options, tenant);
        db.Products.Add(new Product { Code = "U" + Guid.NewGuid().ToString("N")[..8], Name = "Ürün", CategoryId = categoryId, BrandId = brandId });
        await db.SaveChangesAsync();
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

internal static class TaskJson {
    public static async Task<JsonElement> JsonBody(this Task<HttpResponseMessage> response) {
        using var r = await response;
        return await r.JsonAsync();
    }
}
