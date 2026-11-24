using ERP.Application.Common.Interfaces;
using ERP.Domain.Inventory;
using ERP.Infrastructure.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

/// <summary>P3 son inceleme düzeltmeleri (I1-I5): gerçek paralel istekler, gerçek PostgreSQL.</summary>
public class ReviewFixTests : TestBase {
    /// <summary>Probe isteği; delayMs kadar kilitleri onaydan önce tutar.</summary>
    private static Task<HttpResponseMessage> Slow(HttpClient client, string op, object body, int delayMs) {
        return client.PostAsJsonAsync($"{Probe}?op={op}&delayMs={delayMs}", body);
    }

    private static Task<int> RunWorker(Env env) {
        return TestEnvironment.Factory.Services.GetRequiredService<RevaluationWorker>().PublicRunOnceAsync(CancellationToken.None, env.Tenant);
    }

    // ---- I1: transfer hedef anahtarı kilitsiz değişiyordu ----------------------------------------------------

    [Test]
    public async Task I1_RevaluationWorker_LocksTransferDestination_AndMergesJobDates() {
        var env = await NewEnv();
        var w2  = await CreateWarehouse(env.Client, env.Branch, "W2");
        await Receive(env, 10m, 10m, D3);
        await Ok(Op(env.Client, "transfer", new {
            sourceType = "test", sourceId = Guid.CreateVersion7(), date = D5.ToString("yyyy-MM-dd"),
            lines = new[] {
                new {
                    fromWarehouseId = env.Warehouse, toWarehouseId = w2, productId = env.Product, variantId = (Guid?)null, baseQuantity = 5m,
                    lots = (object?)null, serials = (object?)null, sourceLineId = (Guid?)null
                }
            }
        }));
        await Receive(env, 10m, 20m, D1); // geriye dönük: kaynak depo için iş (D1)
        (await Jobs(env.Tenant)).Single().WarehouseId.ShouldBe(env.Warehouse);

        // Hedef depoya canlı geriye dönük giriş: hedef kilidini ve (onaylanmamış) hedef iş kaydını tutar.
        var live = Slow(env.Client, "receive", Doc("Receipt", D3, Line(w2, env.Product, 4m, 8m)), 1500);
        await Task.Delay(400);
        var       worked       = await RunWorker(env);
        using var liveResponse = await live;
        liveResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        worked.ShouldBeGreaterThanOrEqualTo(2); // kaynak + hedef işi
        (await Jobs(env.Tenant)).ShouldBeEmpty();
        var movements = await Movements(env.Tenant);
        movements.Single(m => m.Type == MovementType.TransferIn).UnitCost.ShouldBe(15m);
        await using var db  = PricingTestSupport.Db(env.Tenant);
        var             snp = await db.AverageCosts.AsNoTracking().SingleAsync(s => s.WarehouseId == w2);
        snp.Quantity.ShouldBe(9m);
        snp.AverageCost.ShouldBe(11.888889m); // (4 x 8 + 5 x 15) / 9
    }

    // ---- I2: çoklu çağrılı yazma yolları kilit sırası ---------------------------------------------------------

    [Test]
    public async Task I2_CountPost_AgainstMultiLineDocuments_DoesNotDeadlock() {
        var env      = await NewEnv();
        var products = new List<Guid> { env.Product };
        for (var i = 1; i < 16; i++) products.Add(await CreateProduct(env.Client, "C" + i));

        object[] AllLines(decimal qty) {
            return products.Select(p => Line(env.Warehouse, p, qty, 10m)).ToArray();
        }

        await Ok(Op(env.Client, "receive", Doc("Receipt", D1, AllLines(10m))));

        for (var round = 0; round < 6; round++) {
            using var created = await env.Client.PostAsJsonAsync($"{Base}/counts", new { warehouseId = env.Warehouse, categoryId = (Guid?)null, name = "r" + round });
            var       count   = (await DocumentSupport.Created(created)).GetProperty("id").GetGuid();
            using var get     = await env.Client.GetAsync($"{Base}/counts/{count}");
            var       lines   = (await get.JsonAsync()).GetProperty("lines").EnumerateArray().ToList();
            var r             = round;
            var payload = lines.Select((l, i) => new {
                productId  = l.GetProperty("productId").GetGuid(), variantId = (Guid?)null,
                countedQty = l.GetProperty("expectedQty").GetDecimal() + ((i + r) % 2 == 0 ? 1m : -1m)
            }).ToList();
            using var set = await env.Client.PutAsJsonAsync($"{Base}/counts/{count}/lines", new { lines = payload });
            set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());

            var tasks = new List<Task<HttpStatusCode>> {
                Task.Run(async () => {
                             using var response = await env.Client.PostAsync($"{Base}/counts/{count}/post", null);
                             return response.StatusCode;
                         })
            };
            for (var d = 0; d < 4; d++) {
                var delay = d * 6;
                tasks.Add(Task.Run(async () => {
                              await Task.Delay(delay);
                              using var response = await Op(env.Client, "receive", Doc("Receipt", D1, AllLines(1m)));
                              return response.StatusCode;
                          }));
            }

            var results = await Task.WhenAll(tasks);
            results[0].ShouldBe(HttpStatusCode.NoContent, "round " + round);
            results.Skip(1).ShouldAllBe(s => s == HttpStatusCode.OK);
        }
    }

    // ---- I3: aynı seri iki varyant/depoya aynı anda girmemeli --------------------------------------------------

    [Test]
    public async Task I3_SameSerial_DifferentVariantsAndWarehouses_OnlyOneReceipt() {
        var env     = await NewEnv(["seri-lot"]);
        var created = await CreateOk(env.Client, new {
            kind = "product", name = "Seri", code = "SV1", tracksSerialLot = true, trackingMode = "serial",
            attributes = new[] { new { name = "Beden", values = new[] { "S", "M" } } }
        });
        var product  = created.GetProperty("id").GetGuid();
        var variants = created.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ToList();
        var w2       = await CreateWarehouse(env.Client, env.Branch, "W2");

        object SLine(Guid warehouse, Guid variant) {
            return new {
                warehouseId = warehouse, productId = product, variantId = (Guid?)variant, baseQuantity = 1m, unitCost = 10m,
                lots = (object?)null, serials = new[] { "SX" }, sourceLineId = (Guid?)null, exemptFromStockCheck = false
            };
        }

        await Ok(Op(env.Client, "receive", Doc("Receipt", D1, SLine(env.Warehouse, variants[0]))));
        await Ok(Op(env.Client, "issue", Doc("Issue", D3, SLine(env.Warehouse, variants[0]))));

        var       first  = Slow(env.Client, "receive", Doc("Receipt", D5, SLine(env.Warehouse, variants[0])), 1200);
        await Task.Delay(300);
        using var second        = await Op(env.Client, "receive", Doc("Receipt", D5, SLine(w2, variants[1])));
        using var firstResponse = await first;

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await firstResponse.Content.ReadAsStringAsync());
        await Fail(second, HttpStatusCode.Conflict, "serial_in_stock");
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.SerialNumbers.AsNoTracking().SingleAsync(s => s.Serial == "SX")).WarehouseId.ShouldBe(env.Warehouse);
        (await Movements(env.Tenant)).Where(m => m.Quantity > 0).Sum(m => m.Quantity).ShouldBe(2m); // ilk giriş + yeniden giriş
    }

    // ---- I4: sayım anlık görüntüsü onaylanmamış yazıları kaçırmamalı -------------------------------------------

    [Test]
    public async Task I4_CountSnapshot_WaitsForInFlightWrites() {
        var env = await NewEnv();
        await Receive(env, 10m);

        var inFlight = Slow(env.Client, "receive", Doc("Receipt", D1, Line(env.Warehouse, env.Product, 5m, 10m)), 1200);
        await Task.Delay(300);
        using var created  = await env.Client.PostAsJsonAsync($"{Base}/counts", new { warehouseId = env.Warehouse, categoryId = (Guid?)null, name = "yarış" });
        var       count    = (await DocumentSupport.Created(created)).GetProperty("id").GetGuid();
        using var response = await inFlight;
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var get  = await env.Client.GetAsync($"{Base}/counts/{count}");
        var       line = (await get.JsonAsync()).GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("expectedQty").GetDecimal().ShouldBe(15m); // yazı onaylanmadan alınan görüntü 10 görürdü

        // Sayılan 15: fark yok; çift sayım olsaydı +5 düzeltme yazılırdı.
        using var set = await env.Client.PutAsJsonAsync($"{Base}/counts/{count}/lines",
                                                        new { lines = new[] { new { productId = env.Product, variantId = (Guid?)null, countedQty = (decimal?)15m } } });
        set.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var post = await env.Client.PostAsync($"{Base}/counts/{count}/post", null);
        post.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await BalanceOf(env, env.Warehouse)).GetProperty("physical").GetDecimal().ShouldBe(15m);
    }

    // ---- I5: her aktif şubenin varsayılan deposu ---------------------------------------------------------------

    [Test]
    public async Task I5_WarehouseList_EnsuresDefaultWarehouseForEveryActiveBranch_Idempotently() {
        var tenant = Guid.CreateVersion7();
        var a      = Guid.CreateVersion7();
        var b      = Guid.CreateVersion7();
        var off    = Guid.CreateVersion7();
        SeedBranches(tenant, Branch(a, "Merkez", "MRK", true), Branch(b, "Şube 2", "SB2"), new BranchRef(off, "PAS", "Pasif", false, false));
        using var client = await AdminClient(tenant);

        for (var i = 0; i < 2; i++) {
            using var list = await client.GetAsync($"{Base}/warehouses");
            list.StatusCode.ShouldBe(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
            var rows = (await list.JsonAsync()).EnumerateArray().ToList();
            rows.Count.ShouldBe(2);
            rows.ShouldAllBe(w => w.GetProperty("isDefault").GetBoolean());
            rows.Select(w => w.GetProperty("branchId").GetGuid()).Order().ToList().ShouldBe(new[] { a, b }.Order().ToList());
        }
    }
}
