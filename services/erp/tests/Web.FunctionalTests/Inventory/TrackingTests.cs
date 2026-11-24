using System.Text.Json;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class TrackingTests : TestBase {
    private const string Module = "seri-lot";

    private static readonly DateOnly Early = new(2027, 1, 1);
    private static readonly DateOnly Late  = new(2027, 6, 1);
    private static readonly DateOnly D7    = new(2026, 3, 7);

    private static object LotIn(string lotNo, decimal quantity, DateOnly? expiry = null) {
        return new { lotNo, expiry = expiry?.ToString("yyyy-MM-dd"), quantity, lotId = (Guid?)null };
    }

    private static object LotOut(Guid lotId, decimal quantity) {
        return new { lotNo = "", expiry = (string?)null, quantity, lotId };
    }

    private static object TLine(Guid warehouse, Guid product, decimal quantity, object[]? lots = null, string[]? serials = null,
                                decimal? cost = 10m) {
        return new {
            warehouseId = warehouse, productId = product, variantId = (Guid?)null, baseQuantity = quantity, unitCost = cost,
            lots, serials, sourceLineId = (Guid?)null, exemptFromStockCheck = false
        };
    }

    private static async Task<(Env Env, Guid Lot, Guid Serial)> NewTracked() {
        var env    = await NewEnv([Module]);
        var lot    = await CreateProduct(env.Client, "LOT-1", new { tracksSerialLot = true, trackingMode = "lot" });
        var serial = await CreateProduct(env.Client, "SER-1", new { tracksSerialLot = true, trackingMode = "serial" });
        return (env, lot, serial);
    }

    private static async Task<JsonElement> Get(HttpClient client, string url) {
        using var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static Task<HttpResponseMessage> ReceiveLots(Env env, Guid product, decimal quantity, params object[] lots) {
        return Op(env.Client, "receive", Doc("Receipt", D1, TLine(env.Warehouse, product, quantity, lots)));
    }

    private static Task<HttpResponseMessage> ReceiveSerials(Env env, Guid product, Guid? warehouse, params string[] serials) {
        return Op(env.Client, "receive", Doc("Receipt", D1, TLine(warehouse ?? env.Warehouse, product, serials.Length, serials: serials)));
    }

    [Test]
    public async Task LotProduct_ReceiptWithoutLots_Returns400LotRequired() {
        var (env, lot, _) = await NewTracked();

        using var response = await Op(env.Client, "receive", Doc("Receipt", D1, TLine(env.Warehouse, lot, 5m)));

        await Fail(response, HttpStatusCode.BadRequest, "lot_required");
        (await Movements(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task LotQuantitiesMustSumToLine_Returns400() {
        var (env, lot, _) = await NewTracked();

        using var response = await ReceiveLots(env, lot, 5m, LotIn("A", 2m), LotIn("B", 2m));

        await Fail(response, HttpStatusCode.BadRequest, "lot_quantity_mismatch");
    }

    [Test]
    public async Task SerialQuantityMustMatchSerialCount_Returns400() {
        var (env, _, serial) = await NewTracked();

        using var response = await Op(env.Client, "receive", Doc("Receipt", D1, TLine(env.Warehouse, serial, 2m, serials: ["S1"])));
        await Fail(response, HttpStatusCode.BadRequest, "serial_quantity_mismatch");

        using var missing = await Op(env.Client, "receive", Doc("Receipt", D1, TLine(env.Warehouse, serial, 1m)));
        await Fail(missing, HttpStatusCode.BadRequest, "serial_required");
    }

    [Test]
    public async Task LotReceipt_CreatesLotsAndReverseRemovesStock() {
        var (env, lot, _) = await NewTracked();
        var sourceId      = Guid.CreateVersion7();
        await Ok(Op(env.Client, "receive", new { type = "Receipt", sourceType = "test", sourceId, date = "2026-03-01", lines = new[] { TLine(env.Warehouse, lot, 5m, [LotIn("A", 3m, Late), LotIn("B", 2m)]) } }));

        var lots = await Get(env.Client, $"{Base}/tracking/lots?productId={lot}&warehouseId={env.Warehouse}");
        lots.GetArrayLength().ShouldBe(2);
        lots[0].GetProperty("lotNo").GetString().ShouldBe("A");
        lots[0].GetProperty("available").GetDecimal().ShouldBe(3m);

        await Ok(Op(env.Client, "reverse", new { type = "Receipt", sourceType = "test", sourceId, date = "2026-03-02" }));

        (await Get(env.Client, $"{Base}/tracking/lots?productId={lot}&warehouseId={env.Warehouse}")).GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task SerialReceiptAlreadyInStock_Returns409() {
        var (env, _, serial) = await NewTracked();
        var other            = await CreateWarehouse(env.Client, env.Branch, "B2");
        await Ok(ReceiveSerials(env, serial, null, "S1", "S2"));

        using var again = await ReceiveSerials(env, serial, null, "S2");
        await Fail(again, HttpStatusCode.Conflict, "serial_in_stock");

        using var elsewhere = await ReceiveSerials(env, serial, other, "S1");
        await Fail(elsewhere, HttpStatusCode.Conflict, "serial_in_stock");

        (await Get(env.Client, $"{Base}/tracking/serials?productId={serial}&inStock=true")).GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task SerialIssueNotInWarehouse_Returns409() {
        var (env, _, serial) = await NewTracked();
        var other            = await CreateWarehouse(env.Client, env.Branch, "B2");
        await Ok(ReceiveSerials(env, serial, null, "S1"));
        await Ok(ReceiveSerials(env, serial, other, "S2")); // diğer depoda bakiye var, ama S1 orada değil

        using var response = await Op(env.Client, "issue", Doc("Issue", D5, TLine(other, serial, 1m, serials: ["S1"])));
        await Fail(response, HttpStatusCode.Conflict, "serial_not_in_stock");

        using var unknown = await Op(env.Client, "issue", Doc("Issue", D5, TLine(other, serial, 1m, serials: ["YOK"])));
        await Fail(unknown, HttpStatusCode.Conflict, "serial_not_in_stock");

        await Ok(Op(env.Client, "issue", Doc("Issue", D5, TLine(other, serial, 1m, serials: ["S2"]))));
        var all = await Get(env.Client, $"{Base}/tracking/serials?productId={serial}&inStock=false");
        all.GetArrayLength().ShouldBe(1);
        all[0].GetProperty("serial").GetString().ShouldBe("S2");
    }

    [Test]
    public async Task TransferMovesSerialLocation() {
        var (env, _, serial) = await NewTracked();
        var other            = await CreateWarehouse(env.Client, env.Branch, "B2");
        await Ok(ReceiveSerials(env, serial, null, "S1", "S2"));

        await Ok(Op(env.Client, "transfer", new {
            sourceType = "transfer", sourceId = Guid.CreateVersion7(), date = "2026-03-05",
            lines = new[] { new { fromWarehouseId = env.Warehouse, toWarehouseId = other, productId = serial, variantId = (Guid?)null, baseQuantity = 1m, lots = (object?)null, serials = new[] { "S1" }, sourceLineId = (Guid?)null } }
        }));

        var moved = await Get(env.Client, $"{Base}/tracking/serials?productId={serial}&warehouseId={other}&inStock=true");
        moved.GetArrayLength().ShouldBe(1);
        moved[0].GetProperty("serial").GetString().ShouldBe("S1");
        (await Get(env.Client, $"{Base}/tracking/serials?productId={serial}&warehouseId={env.Warehouse}&inStock=true")).GetArrayLength().ShouldBe(1);

        // Eski depodan artık çıkamaz, yenisinden çıkabilir.
        using var oldPlace = await Op(env.Client, "issue", Doc("Issue", D7, TLine(env.Warehouse, serial, 1m, serials: ["S1"])));
        await Fail(oldPlace, HttpStatusCode.Conflict, "serial_not_in_stock");
        await Ok(Op(env.Client, "issue", Doc("Issue", D7, TLine(other, serial, 1m, serials: ["S1"]))));
    }

    [Test]
    public async Task LotTransfer_MovesLotBalance() {
        var (env, lot, _) = await NewTracked();
        var other         = await CreateWarehouse(env.Client, env.Branch, "B2");
        await Ok(ReceiveLots(env, lot, 5m, LotIn("A", 5m, Early)));
        var lotId = (await Get(env.Client, $"{Base}/tracking/lots?productId={lot}&warehouseId={env.Warehouse}"))[0].GetProperty("lotId").GetGuid();

        await Ok(Op(env.Client, "transfer", new {
            sourceType = "transfer", sourceId = Guid.CreateVersion7(), date = "2026-03-05",
            lines = new[] { new { fromWarehouseId = env.Warehouse, toWarehouseId = other, productId = lot, variantId = (Guid?)null, baseQuantity = 2m, lots = new object[] { LotOut(lotId, 2m) }, serials = (object?)null, sourceLineId = (Guid?)null } }
        }));

        (await Get(env.Client, $"{Base}/tracking/lots?productId={lot}&warehouseId={other}"))[0].GetProperty("available").GetDecimal().ShouldBe(2m);
        (await Get(env.Client, $"{Base}/tracking/lots?productId={lot}&warehouseId={env.Warehouse}"))[0].GetProperty("available").GetDecimal().ShouldBe(3m);
    }

    [Test]
    public async Task LotIssueMoreThanLot_Returns409LotInsufficient() {
        var (env, lot, _) = await NewTracked();
        await Ok(ReceiveLots(env, lot, 5m, LotIn("A", 5m, Early)));
        await Ok(ReceiveLots(env, lot, 5m, LotIn("B", 5m, Late)));
        var lots = await Get(env.Client, $"{Base}/tracking/lots?productId={lot}&warehouseId={env.Warehouse}");
        var lotA = lots[0].GetProperty("lotId").GetGuid();

        using var response = await Op(env.Client, "issue", Doc("Issue", D5, TLine(env.Warehouse, lot, 6m, [LotOut(lotA, 6m)])));
        await Fail(response, HttpStatusCode.Conflict, "lot_insufficient");

        using var withoutLots = await Op(env.Client, "issue", Doc("Issue", D5, TLine(env.Warehouse, lot, 1m)));
        await Fail(withoutLots, HttpStatusCode.BadRequest, "lot_required");

        await Ok(Op(env.Client, "issue", Doc("Issue", D5, TLine(env.Warehouse, lot, 4m, [LotOut(lotA, 4m)]))));
        (await Get(env.Client, $"{Base}/tracking/lots?productId={lot}&warehouseId={env.Warehouse}"))[0].GetProperty("available").GetDecimal().ShouldBe(1m);
    }

    [Test]
    public async Task Suggest_FefoOrder() {
        var (env, lot, _) = await NewTracked();
        await Ok(ReceiveLots(env, lot, 5m, LotIn("NOEXP", 5m)));
        await Ok(ReceiveLots(env, lot, 5m, LotIn("LATE", 5m, Late)));
        await Ok(ReceiveLots(env, lot, 5m, LotIn("EARLY", 5m, Early)));

        var picks = await Get(env.Client, $"{Base}/tracking/lots/suggest?productId={lot}&warehouseId={env.Warehouse}&qty=12");

        picks.EnumerateArray().Select(p => p.GetProperty("lotNo").GetString()).ShouldBe(["EARLY", "LATE", "NOEXP"]);
        picks.EnumerateArray().Select(p => p.GetProperty("quantity").GetDecimal()).ShouldBe([5m, 5m, 2m]);

        var partial = await Get(env.Client, $"{Base}/tracking/lots/suggest?productId={lot}&warehouseId={env.Warehouse}&qty=3");
        partial.GetArrayLength().ShouldBe(1);
        partial[0].GetProperty("quantity").GetDecimal().ShouldBe(3m);
    }

    [Test]
    public async Task TrackingWithoutModule_Returns403() {
        var env = await NewEnv();

        foreach (var url in new[] {
                     $"{Base}/tracking/lots?productId={env.Product}",
                     $"{Base}/tracking/lots/suggest?productId={env.Product}&warehouseId={env.Warehouse}&qty=1",
                     $"{Base}/tracking/serials?productId={env.Product}"
                 }) {
            using var response = await env.Client.GetAsync(url);
            await Fail(response, HttpStatusCode.Forbidden, "module_disabled");
        }
    }
}
