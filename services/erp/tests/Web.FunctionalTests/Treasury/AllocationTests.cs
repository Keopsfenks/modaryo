using ERP.Domain.CurrentAccounts;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Treasury.PaymentSupport;

namespace ERP.Web.FunctionalTests.Treasury;

public class AllocationTests : TestBase {
    private static readonly DateOnly Older = new(2026, 2, 1);
    private static readonly DateOnly Newer = new(2026, 3, 1);

    private static async Task<decimal> OpenOf(Env env, Guid cari, string documentNo) {
        var items = await OpenItems(env, cari);
        return items.Where(i => i.GetProperty("documentNo").GetString() == documentNo).Sum(i => i.GetProperty("open").GetDecimal());
    }

    [Test]
    public async Task ManualAllocation_ToSpecificInvoice() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var a    = await Invoice(env, 100m, Older);
        var b    = await Invoice(env, 100m, Newer);
        var payment = await CollectOk(env, kasa, 100m); // FIFO: önce eski fatura A kapanır
        (await OpenOf(env, env.Customer, a.Number)).ShouldBe(0m);
        (await OpenOf(env, env.Customer, b.Number)).ShouldBe(100m);

        using var response = await Allocate(env, payment, null, (await LineOf(env, env.Customer, b.Number), 100m));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await OpenOf(env, env.Customer, b.Number)).ShouldBe(0m);
        (await OpenOf(env, env.Customer, a.Number)).ShouldBe(100m);
        var manual = (await Allocations(env.Tenant, AllocationKind.Manual)).Single();
        manual.Amount.ShouldBe(100m);
        (await Allocations(env.Tenant, AllocationKind.Auto)).ShouldBeEmpty();
    }

    [Test]
    public async Task ManualAllocationAtCreation_AndUnallocate() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var a    = await Invoice(env, 100m, Older);
        var b    = await Invoice(env, 100m, Newer);

        // Belge oluşturulurken elle eşleştirme; kalan tutar FIFO ile akar.
        await CollectOk(env, kasa, 150m, extra: new { allocations = new[] { new { dueLineId = await LineOf(env, env.Customer, b.Number), amount = 60m } } });

        (await OpenOf(env, env.Customer, b.Number)).ShouldBe(40m);
        (await OpenOf(env, env.Customer, a.Number)).ShouldBe(10m);
        var manual = (await Allocations(env.Tenant, AllocationKind.Manual)).Single();

        using var remove = await env.Client.DeleteAsync($"{Base}/allocations/{manual.Id}");
        remove.StatusCode.ShouldBe(HttpStatusCode.NoContent, await remove.Content.ReadAsStringAsync());

        (await Allocations(env.Tenant, AllocationKind.Manual)).ShouldBeEmpty();
        (await OpenOf(env, env.Customer, a.Number)).ShouldBe(0m);
        (await OpenOf(env, env.Customer, b.Number)).ShouldBe(50m);

        var auto = (await Allocations(env.Tenant, AllocationKind.Auto)).First();
        using var notManual = await env.Client.DeleteAsync($"{Base}/allocations/{auto.Id}");
        await Fail(notManual, HttpStatusCode.Conflict, "allocation_not_manual");
        using var missing = await env.Client.DeleteAsync($"{Base}/allocations/{manual.Id}");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ManualAllocationSurvivesBackdatedOlderInvoice() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var b    = await Invoice(env, 100m, Newer);
        var payment = await CollectOk(env, kasa, 100m);
        await AllocateOk(env, payment, null, (await LineOf(env, env.Customer, b.Number), 100m));
        (await Allocations(env.Tenant, AllocationKind.Manual)).Single().Amount.ShouldBe(100m);

        // Geriye dönük, daha eski bir fatura: elle eşleştirilen tutarı çalamaz.
        var a = await Invoice(env, 100m, new DateOnly(2026, 1, 5));

        (await OpenOf(env, env.Customer, b.Number)).ShouldBe(0m);
        (await OpenOf(env, env.Customer, a.Number)).ShouldBe(100m);
        (await Allocations(env.Tenant, AllocationKind.Manual)).Single().Amount.ShouldBe(100m);
        // Yalnızca eşleştirilmemiş artık FIFO akar: yeni bir tahsilat eski faturayı kapatır.
        await CollectOk(env, kasa, 100m);
        (await OpenOf(env, env.Customer, a.Number)).ShouldBe(0m);
    }

    [Test]
    public async Task AllocationExceedsOpen_Returns400() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var a    = await Invoice(env, 100m, Older);
        var b    = await Invoice(env, 100m, Newer);
        var payment = await CollectOk(env, kasa, 120m);
        var lineA   = await LineOf(env, env.Customer, a.Number);
        var lineB   = await LineOf(env, env.Customer, b.Number);

        // Satır açık tutarını aşıyor.
        using var overLine = await Allocate(env, payment, null, (lineB, 100.01m));
        await Fail(overLine, HttpStatusCode.BadRequest, "allocation_exceeds_open");
        // Toplam, kaynağın tutarını aşıyor.
        using var overSource = await Allocate(env, payment, null, (lineA, 100m), (lineB, 30m));
        await Fail(overSource, HttpStatusCode.BadRequest, "allocation_exceeds_open");

        // Kısmi eşleştirmeler birikir: 50 + 50 olur, üçüncüsü aşar.
        using var first = await Allocate(env, payment, null, (lineB, 50m));
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        using var second = await Allocate(env, payment, null, (lineB, 50m));
        second.StatusCode.ShouldBe(HttpStatusCode.Created, await second.Content.ReadAsStringAsync());
        using var third = await Allocate(env, payment, null, (lineB, 1m));
        await Fail(third, HttpStatusCode.BadRequest, "allocation_exceeds_open");
        (await Allocations(env.Tenant, AllocationKind.Manual)).Sum(x => x.Amount).ShouldBe(100m);

        // Biçim hataları.
        using var zero = await Allocate(env, payment, null, (lineA, 0m));
        zero.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var dupe = await Allocate(env, payment, null, (lineA, 5m), (lineA, 5m));
        await Fail(dupe, HttpStatusCode.BadRequest, "allocation_invalid");
        using var none = await env.Client.PostAsJsonAsync($"{Base}/allocations", new { allocations = new[] { new { dueLineId = lineA, amount = 5m } } });
        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AllocationOtherCari_Returns400() {
        var env   = await NewEnv();
        var kasa  = await Account(env, "Merkez Kasa");
        var other = await CreateCari(env.Client, "M2", customer: true, supplier: false);
        await Invoice(env, 100m, Older);
        var payment = await CollectOk(env, kasa, 50m);
        var foreign = await CreateAndIssue(env, Body(env, extra: new { currentAccountId = other }, lines: [FreeLine("Hizmet", 1m, 100m, 20)]));
        var otherLine = await LineOf(env, other, foreign.GetProperty("number").GetString()!);

        using var response = await Allocate(env, payment, null, (otherLine, 10m));

        await Fail(response, HttpStatusCode.BadRequest, "allocation_other_account");
        (await Allocations(env.Tenant, AllocationKind.Manual)).ShouldBeEmpty();
    }

    [Test]
    public async Task AllocationDirection_MustBeOpposite() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var a    = await Invoice(env, 100m, Older);
        var payment1 = await CollectOk(env, kasa, 20m);
        var payment2 = await CollectOk(env, kasa, 20m);
        var credit2Line = (await OpenItems(env, env.Customer)).First(i => i.GetProperty("sourceId").ValueKind != System.Text.Json.JsonValueKind.Null && i.GetProperty("sourceId").GetGuid() == payment2);

        // Alacak satırını alacak satırıyla eşleştirmek anlamsız; fatura (borç) satırına yönelmek gerekir.
        using var sameSide = await Allocate(env, payment1, null, (credit2Line.GetProperty("dueLineId").GetGuid(), 5m));
        await Fail(sameSide, HttpStatusCode.BadRequest, "allocation_direction");
        using var self = await Allocate(env, payment1, null, (credit2Line.GetProperty("dueLineId").GetGuid(), 5m), (await LineOf(env, env.Customer, a.Number), 5m));
        await Fail(self, HttpStatusCode.BadRequest, "allocation_direction");

        // Tedarikçi ödemesi: kaynak borç tarafıdır, hedef alış faturasının alacak satırıdır.
        var purchase = await Invoice(env, 200m, Newer, "Purchase");
        var pay      = await PayOk(env, kasa, 120m);
        using var ok = await Allocate(env, pay, null, (await LineOf(env, env.Supplier, purchase.Number), 70m));
        ok.StatusCode.ShouldBe(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        (await OpenOf(env, env.Supplier, purchase.Number)).ShouldBe(80m);
        var manual = (await Allocations(env.Tenant, AllocationKind.Manual)).Single();
        var debitLine = (await DueLines(env.Tenant)).Single(l => l.Id == manual.DebitLineId);
        debitLine.Direction.ShouldBe(DueDirection.Receivable);
    }

    [Test]
    public async Task CancelledSideIsRejected() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var a    = await Invoice(env, 100m, Older);
        var payment = await CollectOk(env, kasa, 50m);
        var lineA   = await LineOf(env, env.Customer, a.Number);
        using var cancel = await CancelDocument(env, payment);
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var cancelledSource = await Allocate(env, payment, null, (lineA, 10m));
        await Fail(cancelledSource, HttpStatusCode.Conflict, "allocation_line_cancelled");

        var payment2 = await CollectOk(env, kasa, 50m);
        using var cancelInvoice = await CancelInvoice(env, a.Id);
        cancelInvoice.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancelInvoice.Content.ReadAsStringAsync());
        using var cancelledTarget = await Allocate(env, payment2, null, (lineA, 10m));
        await Fail(cancelledTarget, HttpStatusCode.Conflict, "allocation_line_cancelled");
    }

    [Test]
    public async Task InvoiceCancelWithManualAllocation_Returns409() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var a    = await Invoice(env, 100m, Older);
        var b    = await Invoice(env, 100m, Newer);
        var payment = await CollectOk(env, kasa, 100m); // otomatik: A kapanır
        using var autoCancel = await CancelInvoice(env, a.Id); // yalnızca otomatik eşleştirme engellemez
        autoCancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await autoCancel.Content.ReadAsStringAsync());
        var c = await Invoice(env, 100m, Older);
        await AllocateOk(env, payment, null, (await LineOf(env, env.Customer, c.Number), 100m));

        using var blocked = await CancelInvoice(env, c.Id);
        await Fail(blocked, HttpStatusCode.Conflict, "invoice_has_payments");

        // Eşleştirme kaldırılınca iptal edilebilir.
        var manual = (await Allocations(env.Tenant, AllocationKind.Manual)).Single();
        using var remove = await env.Client.DeleteAsync($"{Base}/allocations/{manual.Id}");
        remove.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var allowed = await CancelInvoice(env, c.Id);
        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await allowed.Content.ReadAsStringAsync());
        b.Id.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task InvoiceCancel_AllowedAfterPaymentCancelled() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var a    = await Invoice(env, 100m, Older);
        var payment = await CollectOk(env, kasa, 100m);
        await AllocateOk(env, payment, null, (await LineOf(env, env.Customer, a.Number), 100m));
        using var blocked = await CancelInvoice(env, a.Id);
        await Fail(blocked, HttpStatusCode.Conflict, "invoice_has_payments");

        // Ödeme belgesi iptal edilince eşleştirmesi de gider; fatura iptal edilebilir.
        using var cancel = await CancelDocument(env, payment);
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Allocations(env.Tenant, AllocationKind.Manual)).ShouldBeEmpty();
        using var allowed = await CancelInvoice(env, a.Id);
        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await allowed.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task OpenItems_ShowOpenAndAllocatable() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var a    = await Invoice(env, 100m, Older);
        await CollectOk(env, kasa, 40m);

        var line = (await OpenItems(env, env.Customer)).Single(i => i.GetProperty("documentNo").GetString() == a.Number);

        line.GetProperty("amount").GetDecimal().ShouldBe(100m);
        line.GetProperty("open").GetDecimal().ShouldBe(60m);
        line.GetProperty("allocatable").GetDecimal().ShouldBe(100m); // otomatik eşleştirme elle olanın önüne geçmez
        line.GetProperty("direction").GetString().ShouldBe("receivable");
    }

    // ---- eşzamanlılık -------------------------------------------------------------------------------------------

    [Test]
    public async Task ParallelAllocationsAgainstSameOpenItem_NeverOverAllocate() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var inv  = await Invoice(env, 100m, Newer);
        var line = await LineOf(env, env.Customer, inv.Number);
        var payments = new List<Guid>();
        for (var i = 0; i < 8; i++) payments.Add(await CollectOk(env, kasa, 50m));

        // 8 ödeme x 50, tek 100'lük kalemi paralel hedefler: yalnızca 2'si sığar.
        var responses = await Task.WhenAll(payments.Select(p => Allocate(env, p, null, (line, 50m))));

        var codes = responses.Select(r => r.StatusCode).ToList();
        codes.Count(c => c == HttpStatusCode.Created).ShouldBe(2, string.Join(",", codes));
        codes.Count(c => c == HttpStatusCode.BadRequest).ShouldBe(6, string.Join(",", codes));
        foreach (var r in responses.Where(r => r.StatusCode == HttpStatusCode.BadRequest)) (await r.ErrorCodeAsync()).ShouldBe("allocation_exceeds_open");
        (await Allocations(env.Tenant, AllocationKind.Manual)).Sum(a => a.Amount).ShouldBe(100m);
        (await OpenOf(env, env.Customer, inv.Number)).ShouldBe(0m);
        await AssertNoOverAllocation(env.Tenant);
    }

    [Test]
    public async Task ParallelAllocationsFromSamePayment_NeverExceedPayment() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var lines = new List<Guid>();
        for (var i = 0; i < 6; i++) {
            var inv = await Invoice(env, 100m, Older.AddDays(i));
            lines.Add(await LineOf(env, env.Customer, inv.Number));
        }

        var payment = await CollectOk(env, kasa, 250m);

        var responses = await Task.WhenAll(lines.Select(l => Allocate(env, payment, null, (l, 100m))));

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        created.ShouldBe(2, string.Join(",", responses.Select(r => r.StatusCode)));
        (await Allocations(env.Tenant, AllocationKind.Manual)).Sum(a => a.Amount).ShouldBe(200m);
        await AssertNoOverAllocation(env.Tenant);
    }

    [Test]
    public async Task CancelInvoiceRacingAllocation_NeverBoth() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var allocated = 0;
        var cancelled = 0;
        for (var round = 0; round < 6; round++) {
            var inv     = await Invoice(env, 100m, Older.AddDays(round));
            var payment = await CollectOk(env, kasa, 100m);
            var line    = await LineOf(env, env.Customer, inv.Number);

            var allocate = Allocate(env, payment, null, (line, 100m));
            var cancel   = CancelInvoice(env, inv.Id);
            await Task.WhenAll(allocate, cancel);
            using var allocateResponse = await allocate;
            using var cancelResponse   = await cancel;

            var allocationOk = allocateResponse.StatusCode == HttpStatusCode.Created;
            var cancelOk     = cancelResponse.StatusCode == HttpStatusCode.NoContent;
            (allocationOk ^ cancelOk).ShouldBeTrue($"round {round}: allocate={allocateResponse.StatusCode} cancel={cancelResponse.StatusCode}");
            if (allocationOk) {
                allocated++;
                await Fail(cancelResponse, HttpStatusCode.Conflict, "invoice_has_payments");
            } else {
                cancelled++;
                await Fail(allocateResponse, HttpStatusCode.Conflict, "allocation_line_cancelled");
            }

            // Hiçbir elle eşleştirme iptal edilmiş bir satıra bağlı kalmaz; ödeme iptali ile sonraki tur temiz başlar.
            await AssertNoManualOnCancelled(env.Tenant);
            using var clear = await CancelDocument(env, payment);
            clear.StatusCode.ShouldBe(HttpStatusCode.NoContent, await clear.Content.ReadAsStringAsync());
        }

        (allocated + cancelled).ShouldBe(6);
    }

    [Test]
    public async Task CancelPaymentRacingAllocation_LeavesNoDanglingAllocation() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        for (var round = 0; round < 6; round++) {
            var inv     = await Invoice(env, 100m, Older.AddDays(round));
            var payment = await CollectOk(env, kasa, 100m);
            var line    = await LineOf(env, env.Customer, inv.Number);

            var allocate = Allocate(env, payment, null, (line, 100m));
            var cancel   = CancelDocument(env, payment);
            await Task.WhenAll(allocate, cancel);
            using var allocateResponse = await allocate;
            using var cancelResponse   = await cancel;

            cancelResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancelResponse.Content.ReadAsStringAsync());
            (allocateResponse.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict).ShouldBeTrue(allocateResponse.StatusCode.ToString());
            await AssertNoManualOnCancelled(env.Tenant);
            // Ödeme iptal edildiği için fatura her durumda iptal edilebilir.
            using var clear = await CancelInvoice(env, inv.Id);
            clear.StatusCode.ShouldBe(HttpStatusCode.NoContent, await clear.Content.ReadAsStringAsync());
        }
    }

    private static async Task AssertNoOverAllocation(Guid tenant) {
        var lines       = (await DueLines(tenant)).ToDictionary(l => l.Id, l => l.Amount);
        var allocations = await Allocations(tenant);
        foreach (var group in allocations.SelectMany(a => new[] { (Line: a.DebitLineId, a.Amount), (Line: a.CreditLineId, a.Amount) }).GroupBy(x => x.Line))
            group.Sum(x => x.Amount).ShouldBeLessThanOrEqualTo(lines[group.Key], $"line {group.Key} over-allocated");
    }

    private static async Task AssertNoManualOnCancelled(Guid tenant) {
        var entries = (await Entries(tenant)).ToDictionary(e => e.Id, e => e.IsCancelled);
        var lines   = (await DueLines(tenant)).ToDictionary(l => l.Id, l => entries[l.EntryId]);
        foreach (var allocation in await Allocations(tenant, AllocationKind.Manual)) {
            lines[allocation.DebitLineId].ShouldBeFalse();
            lines[allocation.CreditLineId].ShouldBeFalse();
        }

        await AssertNoOverAllocation(tenant);
    }
}
