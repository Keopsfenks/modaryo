using System.Text.Json;
using ERP.Domain.Purchasing;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Purchasing.PurchasingTestSupport;

namespace ERP.Web.FunctionalTests.Purchasing;

public class OrderTests : TestBase {
    [Test]
    public async Task CreatePo_PricesFromSupplierList() {
        var env  = await NewPurchasingEnv();
        var list = await PricingApi.CreateList(env.Client, "Tedarikçi listesi", "purchase", new { supplierId = env.Supplier, vatIncluded = false });
        using var rows = await PricingApi.PutRows(env.Client, list, PricingApi.Row(env.Product, PricingApi.Tiers((1m, 12.5m))));
        rows.StatusCode.ShouldBe(HttpStatusCode.OK, await rows.Content.ReadAsStringAsync());

        var po = await CreatePo(env, PoBody(env, [PoLine(env.Product, 10m, price: null)]));

        po.GetProperty("status").GetString().ShouldBe("draft");
        po.GetProperty("number").GetString()!.ShouldMatch(@"^SA2026\d{6}$");
        var line = po.GetProperty("lines")[0];
        line.GetProperty("unitPrice").GetDecimal().ShouldBe(12.5m);
        line.GetProperty("net").GetDecimal().ShouldBe(125m);
        line.GetProperty("total").GetDecimal().ShouldBe(150m);
        line.GetProperty("expectedDate").GetString().ShouldBe(Day.ToString("yyyy-MM-dd")); // satır tarihi başlık tarihinden gelir
        po.GetProperty("total").GetDecimal().ShouldBe(150m);
    }

    [Test]
    public async Task CreatePo_NonSupplierCari_Returns400() {
        var env = await NewPurchasingEnv();

        using var response = await env.Client.PostAsJsonAsync($"{Api}/orders", PoBody(env, extra: new { supplierId = env.Customer }));

        await Fail(response, HttpStatusCode.BadRequest, "cari_not_supplier");
    }

    [Test]
    public async Task ApproveThenEdit_Returns409() {
        var env = await NewPurchasingEnv();
        var po  = await CreatePo(env, PoBody(env));
        var id  = po.GetProperty("id").GetGuid();

        var approved = await Approve(env.Client, id);
        using var edit = await env.Client.PutAsJsonAsync($"{Api}/orders/{id}", PoBody(env, [PoLine(env.Product, 3m)]));
        using var again = await env.Client.PostAsync($"{Api}/orders/{id}/approve", null);

        approved.GetProperty("status").GetString().ShouldBe("approved");
        await Fail(edit, HttpStatusCode.Conflict, "po_not_draft");
        await Fail(again, HttpStatusCode.Conflict, "po_not_draft");
    }

    [Test]
    public async Task UpdateDraft_ReplacesLines() {
        var env = await NewPurchasingEnv();
        var po  = await CreatePo(env, PoBody(env));
        var id  = po.GetProperty("id").GetGuid();

        using var response = await env.Client.PutAsJsonAsync($"{Api}/orders/{id}", PoBody(env, [PoLine(env.Product, 3m, 100m), PoLine(env.Product, 1m, 50m)]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var updated = await response.JsonAsync();
        updated.GetProperty("lines").GetArrayLength().ShouldBe(2);
        updated.GetProperty("net").GetDecimal().ShouldBe(350m);
        updated.GetProperty("number").GetString().ShouldBe(po.GetProperty("number").GetString());
    }

    [Test]
    public async Task CancelWithReceipts_Returns409() {
        var env       = await NewPurchasingEnv();
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var po        = await CreatePo(env, PoBody(env));
        var id        = po.GetProperty("id").GetGuid();
        await Approve(env.Client, id);
        await SeedReceiptAsync(env, id, warehouse);

        using var response = await env.Client.PostAsJsonAsync($"{Api}/orders/{id}/cancel", new { reason = "Vazgeçildi" });

        await Fail(response, HttpStatusCode.Conflict, "po_has_receipts");
    }

    [Test]
    public async Task Cancel_WithoutReceipts_Succeeds() {
        var env = await NewPurchasingEnv();
        var id  = (await CreatePo(env, PoBody(env))).GetProperty("id").GetGuid();
        await Approve(env.Client, id);

        var cancelled = await Post(env.Client, $"orders/{id}/cancel", new { reason = "Vazgeçildi" });

        cancelled.GetProperty("status").GetString().ShouldBe("cancelled");
        cancelled.GetProperty("cancelReason").GetString().ShouldBe("Vazgeçildi");
    }

    [Test]
    public async Task Close_ReleasesOpenQuantity() {
        var env = await NewPurchasingEnv();
        var id  = (await CreatePo(env, PoBody(env))).GetProperty("id").GetGuid();
        await Approve(env.Client, id);

        var closed = await Post(env.Client, $"orders/{id}/close", new { reason = "Tedarikçi gönderemiyor" });

        closed.GetProperty("status").GetString().ShouldBe("closed");
        var line = closed.GetProperty("lines")[0];
        line.GetProperty("closedQuantity").GetDecimal().ShouldBe(10m);
        line.GetProperty("openQuantity").GetDecimal().ShouldBe(0m);
        using var cancel = await env.Client.PostAsJsonAsync($"{Api}/orders/{id}/cancel", new { reason = "Geç kaldı" });
        await Fail(cancel, HttpStatusCode.Conflict, "po_closed");
    }

    [Test]
    public async Task Copy_CreatesDraftWithSameLines() {
        var env = await NewPurchasingEnv();
        var src = await CreatePo(env, PoBody(env, [PoLine(env.Product, 7m, 30m), PoLine(env.Product, 2m, 40m)]));
        var id  = src.GetProperty("id").GetGuid();
        await Approve(env.Client, id);

        var copy = await Post(env.Client, $"orders/{id}/copy", expected: HttpStatusCode.Created);

        copy.GetProperty("id").GetGuid().ShouldNotBe(id);
        copy.GetProperty("status").GetString().ShouldBe("draft");
        copy.GetProperty("number").GetString().ShouldNotBe(src.GetProperty("number").GetString());
        copy.GetProperty("supplierId").GetGuid().ShouldBe(env.Supplier);
        var lines = copy.GetProperty("lines");
        lines.GetArrayLength().ShouldBe(2);
        lines[0].GetProperty("quantity").GetDecimal().ShouldBe(7m);
        lines[1].GetProperty("unitPrice").GetDecimal().ShouldBe(40m);
        lines[0].GetProperty("receivedQuantity").GetDecimal().ShouldBe(0m);
        copy.GetProperty("net").GetDecimal().ShouldBe(src.GetProperty("net").GetDecimal());
    }

    [Test]
    public async Task OverdueFilter() {
        var env   = await NewPurchasingEnv();
        var today = Today();
        var late  = (await CreatePo(env, PoBody(env, extra: new { expectedDate = today.AddDays(-5).ToString("yyyy-MM-dd") }, date: today.AddDays(-10)))).GetProperty("id").GetGuid();
        var later = (await CreatePo(env, PoBody(env, extra: new { expectedDate = today.AddDays(10).ToString("yyyy-MM-dd") }, date: today.AddDays(-10)))).GetProperty("id").GetGuid();
        var draft = (await CreatePo(env, PoBody(env, extra: new { expectedDate = today.AddDays(-5).ToString("yyyy-MM-dd") }, date: today.AddDays(-10)))).GetProperty("id").GetGuid();
        await Approve(env.Client, late);
        await Approve(env.Client, later);

        using var response = await env.Client.GetAsync($"{Api}/orders?overdue=true");
        using var all      = await env.Client.GetAsync($"{Api}/orders");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var items = (await response.JsonAsync()).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("id").GetGuid()).ShouldBe([late]);
        items[0].GetProperty("overdue").GetBoolean().ShouldBeTrue();
        (await all.JsonAsync()).GetProperty("total").GetInt32().ShouldBe(3);
        draft.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task List_FiltersByStatusSupplierAndSearch() {
        var env  = await NewPurchasingEnv();
        var a    = await CreatePo(env, PoBody(env));
        var b    = await CreatePo(env, PoBody(env));
        await Approve(env.Client, b.GetProperty("id").GetGuid());
        var other = await CreateCari(env.Client, "T2", customer: false, supplier: true, name: "Başka Tedarikçi");
        await CreatePo(env, PoBody(env, extra: new { supplierId = other }));

        async Task<JsonElement[]> Items(string query) {
            using var response = await env.Client.GetAsync($"{Api}/orders{query}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.JsonAsync()).GetProperty("items").EnumerateArray().ToArray();
        }

        (await Items("?status=Approved")).Length.ShouldBe(1);
        (await Items($"?supplierId={other}")).Length.ShouldBe(1);
        (await Items("?search=" + a.GetProperty("number").GetString())).Length.ShouldBe(1);
        (await Items("?search=Başka")).Length.ShouldBe(1);
        (await Items("")).Length.ShouldBe(3);
    }

    [Test]
    public async Task LastPurchaseHint_FromIssuedInvoices() {
        var env = await NewPurchasingEnv();
        await CreateSeries(env, "ALS", group: "purchase");
        await CreateAndIssue(env, Body(env, "Purchase", [ProductLine(env, 4m, 25m)])); // 4 x 25 = 100 TL, KDV hariç

        var po = await CreatePo(env, PoBody(env));

        var hint = po.GetProperty("lines")[0].GetProperty("lastPurchase");
        hint.GetProperty("unitPriceTl").GetDecimal().ShouldBe(25m);
        hint.GetProperty("date").GetString().ShouldBe(Day.ToString("yyyy-MM-dd"));
    }

    [Test]
    public async Task LastPurchaseHint_AbsentWithoutInvoices() {
        var env = await NewPurchasingEnv();

        var po = await CreatePo(env, PoBody(env));

        po.GetProperty("lines")[0].GetProperty("lastPurchase").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task WithoutAlis_PricesNull() {
        var env = await NewPurchasingEnv();
        var id  = (await CreatePo(env, PoBody(env))).GetProperty("id").GetGuid();
        await Approve(env.Client, id);
        using var reader = await OtherClient(env, "satinalma.read", "satinalma.manage", "katalog.read");

        var po = await GetPo(reader, id);
        using var list   = await reader.GetAsync($"{Api}/orders");
        using var create = await reader.PostAsJsonAsync($"{Api}/orders", PoBody(env));

        po.GetProperty("total").ValueKind.ShouldBe(JsonValueKind.Null);
        po.GetProperty("net").ValueKind.ShouldBe(JsonValueKind.Null);
        var line = po.GetProperty("lines")[0];
        line.GetProperty("quantity").GetDecimal().ShouldBe(10m);
        line.GetProperty("unitPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        line.GetProperty("total").ValueKind.ShouldBe(JsonValueKind.Null);
        line.GetProperty("lastPurchase").ValueKind.ShouldBe(JsonValueKind.Null);
        po.GetProperty("matchRows")[0].GetProperty("poPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        (await list.JsonAsync()).GetProperty("items")[0].GetProperty("total").ValueKind.ShouldBe(JsonValueKind.Null);
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Permissions_ReadOnlyCannotWrite_AndBranchScopeApplies() {
        var env = await NewPurchasingEnv();
        var id  = (await CreatePo(env, PoBody(env))).GetProperty("id").GetGuid();
        using var reader = await OtherClient(env, "satinalma.read", "fiyat.alis");
        using var none   = await OtherClient(env, "katalog.read");

        using var approve = await reader.PostAsync($"{Api}/orders/{id}/approve", null);
        using var read    = await reader.GetAsync($"{Api}/orders/{id}");
        using var denied  = await none.GetAsync($"{Api}/orders");

        approve.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task MatchRows_ShowOrderedReceivedAndInvoiced() {
        var env = await NewPurchasingEnv();
        var po  = await CreatePo(env, PoBody(env));

        var row = po.GetProperty("matchRows")[0];

        row.GetProperty("ordered").GetDecimal().ShouldBe(10m);
        row.GetProperty("received").GetDecimal().ShouldBe(0m);
        row.GetProperty("invoiced").GetDecimal().ShouldBe(0m);
        row.GetProperty("poPrice").GetDecimal().ShouldBe(20m);
        po.GetProperty("receipts").GetArrayLength().ShouldBe(0);
        po.GetProperty("invoices").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task PurchasingSettings_RoundTrip() {
        var env = await NewPurchasingEnv();
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "KRN", "Quarantine");

        using var defaults = await env.Client.GetAsync("/api/erp/settings/satinalma");
        var initial = (await defaults.JsonAsync()).GetProperty("value");
        using var put = await env.Client.PutAsJsonAsync("/api/erp/settings/satinalma", new {
            version = 0,
            value = new {
                overDeliveryPercent = 5m, priceVariancePercent = 3m, quarantineEnabled = true,
                quarantineWarehouseByBranch = new Dictionary<Guid, Guid> { [env.Branch] = warehouse }, defaultLandedKey = "weight"
            }
        });
        using var denied = await (await OtherClient(env, "satinalma.read")).GetAsync("/api/erp/settings/satinalma");

        initial.GetProperty("overDeliveryPercent").GetDecimal().ShouldBe(0m);
        initial.GetProperty("priceVariancePercent").GetDecimal().ShouldBe(2m);
        initial.GetProperty("quarantineEnabled").GetBoolean().ShouldBeFalse();
        initial.GetProperty("quarantineWarehouseByBranch").EnumerateObject().Count().ShouldBe(0);
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var saved = (await put.JsonAsync()).GetProperty("value");
        saved.GetProperty("overDeliveryPercent").GetDecimal().ShouldBe(5m);
        saved.GetProperty("defaultLandedKey").GetString().ShouldBe("weight");
        saved.GetProperty("quarantineWarehouseByBranch").GetProperty(env.Branch.ToString()).GetGuid().ShouldBe(warehouse);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
