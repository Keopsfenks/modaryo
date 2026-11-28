using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Pos;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Treasury;

namespace ERP.Web.FunctionalTests.Pos;

internal static class PosTestSupport {
    public const string Api = "/api/erp/pos";

    public static readonly string[] Permissions = [
        "pos.sell", "pos.return", "pos.shift", "pos.cashmove", "pos.reports", "pos.manage", "ayarlar.pos",
        "finans.read", "finans.manage", "ayarlar.ebelge", "ayarlar.finans", "ayarlar.stok", "katalog.read", "katalog.manage",
        "cari.read", "cari.manage", "cari.movements", "stok.read", "stok.manage", "fiyat.read", "fiyat.manage", "maliyet.read"
    ];

    public sealed record Env(
        Guid Tenant, Guid Branch, Guid OtherBranch, HttpClient Client, Guid Warehouse, Guid OtherWarehouse, Guid CashAccount, Guid CardAccount);

    /// <summary>İki şubeli firma: Merkez (kasa hesapları ve deposuyla) ve Kadıköy.</summary>
    public static async Task<Env> NewEnv(string[]? permissions = null, string[]? modules = null) {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var other  = Guid.CreateVersion7();
        TestEnvironment.Factory.Branches.Seed(tenant, new BranchRef(branch, "MRK", "Merkez", true, true), new BranchRef(other, "KDK", "Kadıköy", true, false));
        var client    = await TestEnvironment.Factory.ClientAsync(tenant, modules, null, null, permissions ?? Permissions);
        var cash      = (await TreasuryTestSupport.CreateAccount(client, branch, "Merkez Kasa")).GetProperty("id").GetGuid();
        var card      = (await TreasuryTestSupport.CreateAccount(client, branch, "Merkez POS", "cardPos", bankName: "Test Bank")).GetProperty("id").GetGuid();
        var warehouse = await InventoryTestSupport.DefaultWarehouse(client, branch);
        var otherWh   = await InventoryTestSupport.DefaultWarehouse(client, other);
        return new Env(tenant, branch, other, client, warehouse, otherWh, cash, card);
    }

    public static Dictionary<string, object?> RegisterBody(Env env, string code = "K1", object? extra = null) {
        var body = new Dictionary<string, object?> {
            ["code"] = code, ["name"] = "Kasa " + code, ["branchId"] = env.Branch, ["warehouseId"] = env.Warehouse,
            ["cashAccountId"] = env.CashAccount, ["cardAccountId"] = env.CardAccount
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return body;
    }

    public static async Task<JsonElement> CreateRegister(Env env, string code = "K1") {
        using var response = await env.Client.PostAsJsonAsync($"{Api}/registers", RegisterBody(env, code));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> GetSettings(HttpClient client) {
        using var response = await client.GetAsync("/api/erp/settings/pos");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<HttpResponseMessage> PutSettings(HttpClient client, object value) {
        var current = await GetSettings(client);
        return await client.PutAsJsonAsync("/api/erp/settings/pos", new { version = current.GetProperty("version").GetInt32(), value });
    }

    public static async Task SetScaleFormats(HttpClient client) {
        using var response = await PutSettings(client, new {
            scaleBarcodes = new object[] {
                new { prefix = "27", itemCodeLength = 5, valueLength = 5, valueKind = "weightGrams" },
                new { prefix = "28", itemCodeLength = 5, valueLength = 5, valueKind = "priceKurus" }
            }
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static async Task<Guid> Product(HttpClient client, string code, object? extra = null, decimal price = 100m, bool vatIncluded = false) {
        var body = new Dictionary<string, object?> {
            ["kind"] = "product", ["name"] = "Ürün " + code, ["code"] = code,
            ["vatRate"] = 20, ["baseUnit"] = "adet", ["defaultSalePrice"] = new { amount = price, currency = "TRY", vatIncluded }
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return (await Catalog.CatalogTestSupport.CreateOk(client, body)).GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> Lookup(HttpClient client, string code, HttpStatusCode expected = HttpStatusCode.OK, Guid? registerId = null) {
        using var response = await client.GetAsync($"{Api}/lookup?code={Uri.EscapeDataString(code)}" + (registerId is { } r ? $"&registerId={r}" : ""));
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    /// <summary>Test oturumunun kullanıcısı (session-v1.example.json); vardiyalar bu kullanıcı adına açılır.</summary>
    public static readonly Guid SessionUser = Guid.Parse("0193a6f2-6c1e-7b3a-9d4e-2f5a8c7b1e90");

    /// <summary>Vardiya uçları Görev 4'te geldi; Görev 3 testleri vardiyayı doğrudan veritabanına yazar.</summary>
    public static async Task<Guid> OpenShiftDb(Env env, Guid registerId, decimal opening = 0m, Guid? user = null) {
        await using var db    = PricingTestSupport.Db(env.Tenant);
        var             shift = new Shift { RegisterId = registerId, CashierUserId = user ?? SessionUser, OpenedAt = DateTimeOffset.UtcNow, OpeningCash = opening };
        db.PosShifts.Add(shift);
        await db.SaveChangesAsync();
        return shift.Id;
    }

    public static async Task<(Guid Register, Guid Shift)> RegisterWithShift(Env env, string code = "K1") {
        var register = (await CreateRegister(env, code)).GetProperty("id").GetGuid();
        return (register, await OpenShiftDb(env, register));
    }

    public static async Task ReceiveStock(Env env, Guid product, decimal quantity, decimal cost = 10m) {
        await Inventory.StockTestSupport.Ok(Inventory.StockTestSupport.Op(env.Client, "receive",
                                                                          Inventory.StockTestSupport.Doc("Receipt", new DateOnly(2026, 3, 1), Inventory.StockTestSupport.Line(env.Warehouse, product, quantity, cost))));
    }

    public static object SaleLine(Guid product, decimal quantity = 1m, object? extra = null) {
        var body = new Dictionary<string, object?> { ["productId"] = product, ["quantity"] = quantity };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return body;
    }

    public static object Pay(string method, decimal amount) {
        return new { method, amount };
    }

    public static Task<HttpResponseMessage> PostSale(HttpClient client, Guid register, object[] lines, object[] payments, Guid? clientSaleId = null, Guid? customerId = null) {
        return client.PostAsJsonAsync($"{Api}/sales", new { clientSaleId = clientSaleId ?? Guid.NewGuid(), registerId = register, customerId, lines, payments });
    }

    public static async Task<JsonElement> Sell(HttpClient client, Guid register, object[] lines, object[] payments, Guid? clientSaleId = null, Guid? customerId = null) {
        using var response = await PostSale(client, register, lines, payments, clientSaleId, customerId);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<string?> SellFails(HttpClient client, Guid register, object[] lines, object[] payments, HttpStatusCode status, Guid? customerId = null) {
        using var response = await PostSale(client, register, lines, payments, null, customerId);
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        return await response.ErrorCodeAsync();
    }

    public static async Task<List<ERP.Domain.Treasury.TreasuryMovement>> Movements(Guid tenant, Guid account) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.TreasuryMovements.AsNoTracking().Where(m => m.AccountId == account && m.CancelledAt == null).ToListAsync();
    }

    public static async Task<decimal> Balance(Guid tenant, Guid account) {
        return (await Movements(tenant, account)).Sum(m => m.Amount);
    }

    public static async Task<List<ERP.Domain.Inventory.StockMovement>> StockMovements(Guid tenant, string source) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.StockMovements.AsNoTracking().Where(m => m.SourceType == source).OrderBy(m => m.Sequence).ToListAsync();
    }

    public static async Task<List<PosSale>> AllSales(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.PosSales.AsNoTracking().Include(s => s.Lines).Include(s => s.Payments).OrderBy(s => s.Number).ToListAsync();
    }

    public static async Task<Guid> OpenShiftApi(Env env, Guid register, decimal opening = 0m) {
        using var response = await env.Client.PostAsJsonAsync($"{Api}/shifts/open", new { registerId = register, openingCash = opening });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> SaleLineId(Env env, string saleNumber, int sort = 1) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return await db.PosSaleLines.AsNoTracking().Where(l => l.Sort == sort && db.PosSales.Any(s => s.Id == l.SaleId && s.Number == saleNumber)).Select(l => l.Id).SingleAsync();
    }

    public static Task<HttpResponseMessage> PostReturn(HttpClient client, string saleNumber, Guid saleLine, decimal quantity, object[]? refund = null, object[]? exchangeLines = null,
                                                      object[]? exchangePayments = null, Guid? clientReturnId = null, Guid? registerId = null) {
        return client.PostAsJsonAsync($"{Api}/returns", new {
            clientReturnId, saleNumber, registerId, lines = new[] { new { saleLineId = saleLine, quantity } }, refund = refund ?? [], exchangeLines, exchangePayments
        });
    }

    public static async Task<JsonElement> Return(HttpClient client, string saleNumber, Guid saleLine, decimal quantity, object[]? refund = null, object[]? exchangeLines = null,
                                                 object[]? exchangePayments = null, Guid? clientReturnId = null) {
        using var response = await PostReturn(client, saleNumber, saleLine, quantity, refund, exchangeLines, exchangePayments, clientReturnId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> CloseShift(HttpClient client, Guid shift, decimal counted) {
        using var response = await client.PostAsJsonAsync($"{Api}/shifts/{shift}/close", new { countedCash = counted, note = (string?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }
}
