using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Web.FunctionalTests.Pricing;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;

namespace ERP.Web.FunctionalTests.Invoicing;

internal static class InvoiceTestSupport {
    public const string Url       = "/api/erp/invoices";
    public const string SeriesUrl = "/api/erp/document-series";

    public static readonly DateOnly Day = new(2026, 3, 10);

    public static readonly string[] AllPermissions = [
        "fatura.read", "fatura.manage", "fatura.issue", "fatura.cancel", "ayarlar.belgeler", "ayarlar.ebelge", "ayarlar.finans", "ayarlar.stok", "katalog.read",
        "katalog.manage", "cari.read", "cari.manage", "cari.movements", "fiyat.alis", "kur.manage", "stok.read", "stok.manage"
    ];

    public sealed record Env(Guid Tenant, HttpClient Client, Guid Branch, Guid Customer, Guid Supplier, Guid Product);

    public static async Task<Env> NewEnv(string[]? permissions = null, decimal? maxDiscount = null, string productCode = "P1") {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        TestEnvironment.Factory.Branches.Seed(tenant, [new BranchRef(branch, "MRK", "Merkez", true, true)]);
        var client   = await TestEnvironment.Factory.ClientAsync(tenant, null, null, maxDiscount, permissions ?? AllPermissions);
        var customer = await CreateCari(client, "M1", customer: true, supplier: false);
        var supplier = await CreateCari(client, "T1", customer: false, supplier: true);
        var product  = await CreateProduct(client, productCode, new { vatRate = 20, baseUnit = "adet" });
        return new Env(tenant, client, branch, customer, supplier, product);
    }

    /// <summary>Aynı firmada ikinci bir istemci (farklı izinlerle).</summary>
    public static Task<HttpClient> OtherClient(Env env, params string[] permissions) {
        return TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, permissions);
    }

    public static async Task<Guid> CreateCari(HttpClient client, string code, bool customer, bool supplier, bool taxpayer = false, string currency = "TRY") {
        using var response = await client.PostAsJsonAsync("/api/erp/current-accounts", new {
            code, name = "Cari " + code, kind = "corporate", isCustomer = customer, isSupplier = supplier, isEInvoiceTaxpayer = taxpayer,
            currency, defaultDueDays = 30, taxNumber = (string?)null
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> CreateSeries(Env env, string prefix, string mode = "Paper", string group = "sales", int year = 2026, int? minWidth = null,
                                                       bool isDefault = false) {
        using var response = await env.Client.PostAsJsonAsync(SeriesUrl, new { branchId = env.Branch, kindGroup = group, mode, prefix, year, minWidth, isDefault });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task EnableEBelge(HttpClient client, string scenario = "basic") {
        using var response = await client.PutAsJsonAsync("/api/erp/settings/ebelge",
                                                         new { version = 0, value = new { enabled = true, defaultScenario = scenario, eIrsaliyeEnabled = false } });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static async Task LockPeriod(HttpClient client, string date) {
        using var response = await client.PutAsJsonAsync("/api/erp/settings/finans", new { version = 0, value = new { periodLockDate = date } });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static object ProductLine(Env env, decimal quantity = 2m, decimal price = 50m, object? extra = null, Guid? product = null) {
        return Merge(new Dictionary<string, object?> { ["productId"] = product ?? env.Product, ["quantity"] = quantity, ["unitPrice"] = price }, extra);
    }

    public static object FreeLine(string description = "Hizmet", decimal quantity = 1m, decimal price = 200m, int vatRate = 10, object? extra = null) {
        return Merge(new Dictionary<string, object?> { ["description"] = description, ["quantity"] = quantity, ["unitPrice"] = price, ["vatRate"] = vatRate }, extra);
    }

    private static Dictionary<string, object?> Merge(Dictionary<string, object?> body, object? extra) {
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return body;
    }

    /// <summary>Fatura gövdesi; extra alanları (currency, rate, docDiscountPercent, ...) üzerine yazar.</summary>
    public static Dictionary<string, object?> Body(Env env, string kind = "Sales", object[]? lines = null, object? extra = null, DateOnly? date = null) {
        var supplier = kind is "Purchase" or "PurchaseReturn";
        var body = new Dictionary<string, object?> {
            ["kind"] = kind, ["branchId"] = env.Branch, ["currentAccountId"] = supplier ? env.Supplier : env.Customer,
            ["date"] = (date ?? Day).ToString("yyyy-MM-dd"), ["currency"] = "TRY", ["lines"] = lines ?? [ProductLine(env), FreeLine()]
        };
        if (supplier) body["externalNumber"] = "TED-" + Guid.NewGuid().ToString("N")[..8];
        return Merge(body, extra);
    }

    public static async Task<JsonElement> Create(Env env, Dictionary<string, object?> body) {
        using var response = await env.Client.PostAsJsonAsync(Url, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> Issue(HttpClient client, Guid id) {
        using var response = await client.PostAsync($"{Url}/{id}/issue", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> CreateAndIssue(Env env, Dictionary<string, object?> body) {
        var draft = await Create(env, body);
        return await Issue(env.Client, draft.GetProperty("id").GetGuid());
    }

    public static async Task Fail(HttpResponseMessage response, HttpStatusCode status, string code) {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        (await response.ErrorCodeAsync()).ShouldBe(code);
    }

    public static decimal Dec(JsonElement element, params string[] path) {
        foreach (var name in path) element = element.GetProperty(name);
        return element.GetDecimal();
    }
}
