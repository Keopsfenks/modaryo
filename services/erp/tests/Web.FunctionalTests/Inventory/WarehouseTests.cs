using ERP.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class WarehouseTests : TestBase {
    private static readonly Guid Tenant  = Guid.CreateVersion7();
    private static readonly Guid BranchA = Guid.CreateVersion7();
    private static readonly Guid BranchB = Guid.CreateVersion7();

    [SetUp]
    public void SeedBranchDirectory() {
        SeedBranches(Tenant, Branch(BranchA, "Merkez", "MRK"), Branch(BranchB, "Kadıköy", "KDK"));
    }

    [Test]
    public async Task DefaultWarehouse_AutoCreatedPerBranch() {
        using var client = await Client(Tenant);

        using var first = await client.GetAsync($"{Base}/warehouses/default?branchId={BranchA}");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await first.JsonAsync();
        body.GetProperty("name").GetString().ShouldBe("Merkez Depo");
        body.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        body.GetProperty("kind").GetString().ShouldBe("normal");
        body.GetProperty("branchId").GetGuid().ShouldBe(BranchA);

        var again = await DefaultWarehouse(client, BranchA);
        again.ShouldBe(body.GetProperty("id").GetGuid());
        var other = await DefaultWarehouse(client, BranchB);
        other.ShouldNotBe(again);

        var list = await (await client.GetAsync($"{Base}/warehouses")).JsonAsync();
        list.GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task DefaultWarehouse_UnknownBranch_Returns404() {
        using var client = await Client(Tenant);

        using var response = await client.GetAsync($"{Base}/warehouses/default?branchId={Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Warehouse_OtherBranchNotAccessible_Returns403() {
        using var admin = await Client(Tenant);
        var       inB   = await CreateWarehouse(admin, BranchB, "KDK-2");
        using var onlyA = await Client(Tenant, [BranchA]);

        using var create = await onlyA.PostAsJsonAsync($"{Base}/warehouses", new { branchId = BranchB, code = "X1", name = "X", kind = "Normal" });
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await create.ErrorCodeAsync()).ShouldBe("branch_forbidden");

        using var def = await onlyA.GetAsync($"{Base}/warehouses/default?branchId={BranchB}");
        def.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var update = await onlyA.PutAsJsonAsync($"{Base}/warehouses/{inB}", new { code = "KDK-2", name = "Yeni", kind = "Normal" });
        update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var visible = await (await onlyA.GetAsync($"{Base}/warehouses")).JsonAsync();
        visible.EnumerateArray().Select(w => w.GetProperty("id").GetGuid()).ShouldNotContain(inB);
    }

    [Test]
    public async Task CreateAndUpdate_ValidatesAndKeepsCodeUnique() {
        using var client = await Client(Tenant);
        var       id     = await CreateWarehouse(client, BranchA, "HASAR", "Damaged");

        using var duplicate = await client.PostAsJsonAsync($"{Base}/warehouses", new { branchId = BranchA, code = "HASAR", name = "Başka", kind = "Normal" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ErrorCodeAsync()).ShouldBe("code_taken");

        using var noName = await client.PostAsJsonAsync($"{Base}/warehouses", new { branchId = BranchA, code = "A2", name = " ", kind = "Normal" });
        noName.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var unknownBranch = await client.PostAsJsonAsync($"{Base}/warehouses", new { branchId = Guid.NewGuid(), code = "A3", name = "A", kind = "Normal" });
        unknownBranch.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var update = await client.PutAsJsonAsync($"{Base}/warehouses/{id}", new { code = "HASAR-1", name = "Hasarlı Depo", kind = "Quarantine" });
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await update.JsonAsync();
        body.GetProperty("code").GetString().ShouldBe("HASAR-1");
        body.GetProperty("kind").GetString().ShouldBe("quarantine");
    }

    [Test]
    public async Task DefaultWarehouse_CannotBeDeletedDeactivatedOrChangedFromNormal() {
        using var client = await Client(Tenant);
        var       id     = await DefaultWarehouse(client, BranchA);

        using var delete = await client.DeleteAsync($"{Base}/warehouses/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await delete.ErrorCodeAsync()).ShouldBe("warehouse_is_default");

        using var deactivate = await client.PostAsync($"{Base}/warehouses/{id}/deactivate", null);
        deactivate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await deactivate.ErrorCodeAsync()).ShouldBe("warehouse_is_default");

        using var kind = await client.PutAsJsonAsync($"{Base}/warehouses/{id}", new { code = "MRK-DEPO", name = "Merkez Depo", kind = "Damaged" });
        kind.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await kind.ErrorCodeAsync()).ShouldBe("warehouse_is_default");
    }

    [Test]
    public async Task ActivateDeactivateAndDeleteEmptyWarehouse() {
        using var client = await Client(Tenant);
        var       id     = await CreateWarehouse(client, BranchA, "GEC");

        (await client.PostAsync($"{Base}/warehouses/{id}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Find(await (await client.GetAsync($"{Base}/warehouses")).JsonAsync(), id).GetProperty("isActive").GetBoolean().ShouldBeFalse();
        (await client.PostAsync($"{Base}/warehouses/{id}/activate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Find(await (await client.GetAsync($"{Base}/warehouses")).JsonAsync(), id).GetProperty("isActive").GetBoolean().ShouldBeTrue();

        (await client.DeleteAsync($"{Base}/warehouses/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"{Base}/warehouses/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteWarehouseWithMovements_Returns409() {
        using var client    = await Client(Tenant);
        var       warehouse = await CreateWarehouse(client, BranchA, "DOLU");
        var       product   = await CreateProduct(client, "W1");
        await AddMovementAsync(Tenant, warehouse, product);

        using var response = await client.DeleteAsync($"{Base}/warehouses/{warehouse}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("warehouse_in_use");
        Find(await (await client.GetAsync($"{Base}/warehouses")).JsonAsync(), warehouse).GetProperty("inUse").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task ReadOnlyUser_CannotManageWarehouses() {
        using var client = await TestEnvironment.Factory.ClientAsync(Tenant, null, null, null, ["stok.read"]);

        (await client.GetAsync($"{Base}/warehouses")).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var create = await client.PostAsJsonAsync($"{Base}/warehouses", new { branchId = BranchA, code = "Z", name = "Z", kind = "Normal" });
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ProductWithMovements_DeleteReturnsProductInUse() {
        using var client    = await Client(Tenant);
        var       warehouse = await DefaultWarehouse(client, BranchA);
        var       used      = await CreateProduct(client, "PU1");
        var       free      = await CreateProduct(client, "PU2");
        await AddMovementAsync(Tenant, warehouse, used);

        using var blocked = await client.DeleteAsync($"/api/erp/catalog/products/{used}");

        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.ErrorCodeAsync()).ShouldBe("product_in_use");
        (await (await client.GetAsync($"/api/erp/catalog/products/{used}")).JsonAsync()).GetProperty("inUse").GetBoolean().ShouldBeTrue();
        (await client.DeleteAsync($"/api/erp/catalog/products/{free}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task VariantWithMovements_IsInUse_AndCannotBeRemoved() {
        using var client    = await Client(Tenant);
        var       warehouse = await DefaultWarehouse(client, BranchA);
        var created = await CreateOk(client, new {
            kind = "product", name = "Tişört", code = "TV1",
            attributes = new[] { new { name = "Beden", values = new[] { "S", "M" } } }
        });
        var productId = created.GetProperty("id").GetGuid();
        var variantS = created.GetProperty("variants").EnumerateArray()
            .First(v => v.GetProperty("values").GetProperty("Beden").GetString() == "S").GetProperty("id").GetGuid();
        await AddMovementAsync(Tenant, warehouse, productId, variantS);

        using var response = await client.PutAsJsonAsync($"/api/erp/catalog/products/{productId}/attributes",
                                                         new { attributes = new[] { new { name = "Beden", values = new[] { "M" } } } });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("variant_in_use");
    }

    [Test]
    public async Task BranchWithWarehouse_InUse() {
        using var client    = await Client(Tenant);
        using var scope     = TestEnvironment.Factory.Services.CreateScope();
        var       providers = scope.ServiceProvider.GetServices<IBranchUsageProvider>().ToList();

        async Task<bool> InUse(Guid branch) {
            foreach (var p in providers)
                if (await p.IsInUseAsync(Tenant, branch, CancellationToken.None))
                    return true;
            return false;
        }

        (await InUse(BranchA)).ShouldBeFalse();

        // Yalnızca otomatik varsayılan depo şubeyi kullanımda yapmaz.
        var defaultId = await DefaultWarehouse(client, BranchA);
        (await InUse(BranchA)).ShouldBeFalse();

        // Kullanıcının açtığı depo şubeyi kullanımda yapar.
        await CreateWarehouse(client, BranchB, "KDK-X");
        (await InUse(BranchB)).ShouldBeTrue();

        // Varsayılan depoda hareket varsa şube kullanımdadır.
        var product = await CreateProduct(client, "BU1");
        await AddMovementAsync(Tenant, defaultId, product);
        (await InUse(BranchA)).ShouldBeTrue();
    }
}
