using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Application.CurrentAccounts;
using ERP.Domain.CurrentAccounts;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ERP.Web.FunctionalTests.CurrentAccounts;

public class AllocationTests : TestBase {
    private const string Url = "/api/erp/current-accounts";

    private static Task<HttpClient> Client(Guid tenant) {
        return Factory.ClientAsync(tenant, "cari.read", "cari.manage", "cari.movements");
    }

    private static async Task<Guid> CreateCardAsync(HttpClient client, string code = "C0001") {
        using var response = await client.PostAsJsonAsync(Url, new {
                                                                       code,
                                                                       name           = $"Cari {code}",
                                                                       kind           = "corporate",
                                                                       isCustomer     = true,
                                                                       isSupplier     = true,
                                                                       currency       = "TRY",
                                                                       defaultDueDays = 30
                                                                   });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddAsync(HttpClient client, Guid id, object body) {
        using var response = await client.PostAsJsonAsync($"{Url}/{id}/entries", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("entry").GetProperty("id").GetGuid();
    }

    private static Task<Guid> DebitAsync(HttpClient client, Guid id, decimal amount, string date, string dueDate) {
        return AddAsync(client, id, new { type = "debit", date, amount, dueDate });
    }

    private static Task<Guid> CreditAsync(HttpClient client, Guid id, decimal amount, string date) {
        return AddAsync(client, id, new { type = "credit", date, amount });
    }

    private static async Task CancelAsync(HttpClient client, Guid id, Guid entryId) {
        using var response
            = await client.PostAsJsonAsync($"{Url}/{id}/entries/{entryId}/cancel", new { reason = "Hatalı giriş" });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<List<JsonElement>> DueItemsAsync(HttpClient client, Guid id) {
        using var response = await client.GetAsync($"{Url}/{id}/due-items");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).EnumerateArray().ToList();
    }

    private static ApplicationDbContext Db(Guid tenantId) {
        var tenant = new FixedTenant(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(TestEnvironment.ConnectionString)
            .AddInterceptors(new TenantInterceptor(tenant))
            .Options;
        return new ApplicationDbContext(options, tenant);
    }

    private static async Task<(List<LedgerDueLine> Lines, List<LedgerAllocation> Allocations)> StoredAsync(
        Guid tenant,
        Guid accountId
    ) {
        await using var db = Db(tenant);
        var lines = await db.LedgerDueLines.AsNoTracking().Where(l => l.CurrentAccountId == accountId)
                        .ToListAsync();
        var allocations = await db.LedgerAllocations.AsNoTracking().Where(a => a.CurrentAccountId == accountId)
                              .ToListAsync();
        return (lines, allocations);
    }

    private static async Task<List<LedgerEntry>> EntriesAsync(Guid tenant, Guid accountId) {
        await using var db = Db(tenant);
        return await db.LedgerEntries.AsNoTracking().Where(e => e.CurrentAccountId == accountId).ToListAsync();
    }

    private static async Task ExecuteAsync(string sql) {
        await using var connection = new NpgsqlConnection(TestEnvironment.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static Guid LineOf(IEnumerable<LedgerDueLine> lines, Guid entryId) {
        return lines.Single(l => l.EntryId == entryId).Id;
    }

    private static void ShouldMatchReadTimeFifo(List<JsonElement> items, IEnumerable<LedgerEntry> entries) {
        var expected = DueMatching.OpenItems(entries, CurrentAccountClock.Today(TimeProvider.System));
        items.Select(i => (i.GetProperty("entryId").GetGuid(), i.GetProperty("remaining").GetDecimal(),
                           i.GetProperty("dueDate").GetString()!, i.GetProperty("daysOverdue").GetInt32()))
            .ToList()
            .ShouldBe(expected.Select(e => (e.EntryId, e.Remaining, e.DueDate.ToString("yyyy-MM-dd"), e.DaysOverdue))
                          .ToList());
    }

    /// <summary>Geri doldurma satırları yeniden üretir; satır kimliği dışındaki her alan aynı kalmalıdır.</summary>
    private static string WithoutLineId(JsonElement item) {
        return string.Join("|", item.EnumerateObject().Where(p => p.Name != "dueLineId")
                               .Select(p => $"{p.Name}={p.Value.GetRawText()}"));
    }

    private static void ShouldNotOverAllocate(List<LedgerDueLine> lines, List<LedgerAllocation> allocations) {
        allocations.Sum(a => a.Amount)
            .ShouldBeLessThanOrEqualTo(lines.Where(l => l.Direction == DueDirection.Receivable).Sum(l => l.Amount));
        foreach (var line in lines) {
            var used = allocations.Where(a => a.DebitLineId == line.Id || a.CreditLineId == line.Id)
                .Sum(a => a.Amount);
            used.ShouldBeLessThanOrEqualTo(line.Amount, $"satır {line.Id} fazla eşleşmiş");
        }
    }

    [Test]
    public async Task Entries_EachGetOneDueLine_AndCreditIsAllocated() {
        var       tenant = Guid.CreateVersion7();
        using var client = await Client(tenant);
        var       id     = await CreateCardAsync(client);
        var       debit  = await DebitAsync(client, id, 100m, "2026-01-01", "2026-01-10");
        var       credit = await CreditAsync(client, id, 60m, "2026-01-05");

        var (lines, allocations) = await StoredAsync(tenant, id);

        lines.Count.ShouldBe(2);
        lines.ShouldAllBe(l => l.Sequence == 1);
        var allocation = allocations.ShouldHaveSingleItem();
        allocation.DebitLineId.ShouldBe(LineOf(lines, debit));
        allocation.CreditLineId.ShouldBe(LineOf(lines, credit));
        allocation.Amount.ShouldBe(60m);
        allocation.Kind.ShouldBe(AllocationKind.Auto);
        allocation.TenantId.ShouldBe(tenant);

        var item = (await DueItemsAsync(client, id)).ShouldHaveSingleItem();
        item.GetProperty("dueLineId").GetGuid().ShouldBe(LineOf(lines, debit));
        item.GetProperty("sequence").GetInt32().ShouldBe(1);
        item.GetProperty("remaining").GetDecimal().ShouldBe(40m);
    }

    [Test]
    public async Task CancelAllocatedDebit_ReallocatesCreditToNextDebit() {
        var       tenant = Guid.CreateVersion7();
        using var client = await Client(tenant);
        var       id     = await CreateCardAsync(client);
        var       older  = await DebitAsync(client, id, 100m, "2026-01-01", "2026-01-10");
        var       newer  = await DebitAsync(client, id, 200m, "2026-01-02", "2026-02-10");
        var       credit = await CreditAsync(client, id, 100m, "2026-01-15");

        var (lines, allocations) = await StoredAsync(tenant, id);
        allocations.ShouldHaveSingleItem().DebitLineId.ShouldBe(LineOf(lines, older));

        await CancelAsync(client, id, older);

        (lines, allocations) = await StoredAsync(tenant, id);
        lines.Count.ShouldBe(3); // iptal edilen hareketin satırı kalır
        var moved = allocations.ShouldHaveSingleItem();
        moved.DebitLineId.ShouldBe(LineOf(lines, newer));
        moved.CreditLineId.ShouldBe(LineOf(lines, credit));
        moved.Amount.ShouldBe(100m);

        var item = (await DueItemsAsync(client, id)).ShouldHaveSingleItem();
        item.GetProperty("entryId").GetGuid().ShouldBe(newer);
        item.GetProperty("remaining").GetDecimal().ShouldBe(100m);
    }

    [Test]
    public async Task BackdatedDebit_ReallocatesCreditToEarlierDebit() {
        var       tenant = Guid.CreateVersion7();
        using var client = await Client(tenant);
        var       id     = await CreateCardAsync(client);
        var       late   = await DebitAsync(client, id, 100m, "2026-02-01", "2026-03-01");
        var       credit = await CreditAsync(client, id, 60m, "2026-02-05");

        async Task<List<(Guid Debit, decimal Amount)>> AllocationsAsync() {
            var (lines, allocations) = await StoredAsync(tenant, id);
            allocations.ShouldAllBe(a => a.CreditLineId == LineOf(lines, credit));
            ShouldNotOverAllocate(lines, allocations);
            ShouldMatchReadTimeFifo(await DueItemsAsync(client, id), await EntriesAsync(tenant, id));
            return allocations.Select(a => (lines.Single(l => l.Id == a.DebitLineId).EntryId, a.Amount))
                .OrderBy(a => a.Item1).ToList();
        }

        (await AllocationsAsync()).ShouldBe([(late, 60m)]);

        var earlier = await DebitAsync(client, id, 40m, "2026-01-01", "2026-01-10"); // tutarı güncellenen eşleştirme
        (await AllocationsAsync()).ShouldBe(new List<(Guid, decimal)> { (late, 20m), (earlier, 40m) }
                                                .OrderBy(a => a.Item1).ToList());

        var earliest = await DebitAsync(client, id, 100m, "2025-12-01", "2026-01-05"); // artakalan eşleştirmeler silinir
        (await AllocationsAsync()).ShouldBe([(earliest, 60m)]);
    }

    [Test]
    public async Task CancelAllocatedCredit_ReopensDebit() {
        var       tenant = Guid.CreateVersion7();
        using var client = await Client(tenant);
        var       id     = await CreateCardAsync(client);
        await DebitAsync(client, id, 100m, "2026-01-01", "2026-01-10");
        var credit = await CreditAsync(client, id, 100m, "2026-01-15");
        (await DueItemsAsync(client, id)).ShouldBeEmpty();

        await CancelAsync(client, id, credit);

        (await StoredAsync(tenant, id)).Allocations.ShouldBeEmpty();
        (await DueItemsAsync(client, id)).ShouldHaveSingleItem().GetProperty("remaining").GetDecimal()
            .ShouldBe(100m);
    }

    [Test]
    public async Task ManualAllocation_SurvivesRebuild() {
        var       tenant = Guid.CreateVersion7();
        using var client = await Client(tenant);
        var       id     = await CreateCardAsync(client);
        var       older  = await DebitAsync(client, id, 100m, "2026-01-01", "2026-01-10");
        var       newer  = await DebitAsync(client, id, 100m, "2026-01-02", "2026-01-20");
        var       credit = await CreditAsync(client, id, 100m, "2026-01-15");

        var (lines, _) = await StoredAsync(tenant, id);
        Guid manualId;
        await using (var db = Db(tenant)) {
            await db.LedgerAllocations.Where(a => a.CurrentAccountId == id).ExecuteDeleteAsync();
            var manual = new LedgerAllocation {
                                                  CurrentAccountId = id,
                                                  DebitLineId      = LineOf(lines, newer),
                                                  CreditLineId     = LineOf(lines, credit),
                                                  Amount           = 100m,
                                                  Kind             = AllocationKind.Manual,
                                                  CreatedAt        = DateTimeOffset.UtcNow
                                              };
            db.LedgerAllocations.Add(manual);
            await db.SaveChangesAsync();
            manualId = manual.Id;
        }

        var third = await DebitAsync(client, id, 10m, "2026-01-03", "2026-01-25"); // yeniden eşleştirmeyi tetikler

        var (_, allocations) = await StoredAsync(tenant, id);
        var kept = allocations.ShouldHaveSingleItem();
        kept.Id.ShouldBe(manualId);
        kept.Kind.ShouldBe(AllocationKind.Manual);
        var open = await DueItemsAsync(client, id);
        open.Select(i => i.GetProperty("entryId").GetGuid()).ToList().ShouldBe([older, third]);
        open.Select(i => i.GetProperty("remaining").GetDecimal()).ToList().ShouldBe([100m, 10m]);
    }

    [Test]
    public async Task ManualAllocationOnCancelledLine_IsDeleted() {
        var       tenant = Guid.CreateVersion7();
        using var client = await Client(tenant);
        var       id     = await CreateCardAsync(client);
        var       older  = await DebitAsync(client, id, 100m, "2026-01-01", "2026-01-10");
        var       newer  = await DebitAsync(client, id, 100m, "2026-01-02", "2026-01-20");
        var       credit = await CreditAsync(client, id, 100m, "2026-01-15");

        var (lines, _) = await StoredAsync(tenant, id);
        await using (var db = Db(tenant)) {
            await db.LedgerAllocations.Where(a => a.CurrentAccountId == id).ExecuteDeleteAsync();
            db.LedgerAllocations.Add(new LedgerAllocation {
                                                              CurrentAccountId = id,
                                                              DebitLineId      = LineOf(lines, newer),
                                                              CreditLineId     = LineOf(lines, credit),
                                                              Amount           = 100m,
                                                              Kind             = AllocationKind.Manual,
                                                              CreatedAt        = DateTimeOffset.UtcNow
                                                          });
            await db.SaveChangesAsync();
        }

        await CancelAsync(client, id, newer);

        var (_, allocations) = await StoredAsync(tenant, id);
        var auto = allocations.ShouldHaveSingleItem();
        auto.Kind.ShouldBe(AllocationKind.Auto);
        auto.DebitLineId.ShouldBe(LineOf(lines, older));
        (await DueItemsAsync(client, id)).ShouldBeEmpty();
    }

    [Test]
    public async Task ConcurrentEntriesSameAccount_AllocationsConsistent() {
        var       tenant = Guid.CreateVersion7();
        using var client = await Client(tenant);
        for (var round = 0; round < 5; round++) {
            var id = await CreateCardAsync(client, $"C{round + 1:D4}");
            await DebitAsync(client, id, 100m, "2026-01-01", "2026-01-10");
            await DebitAsync(client, id, 100m, "2026-01-02", "2026-01-20");
            await DebitAsync(client, id, 100m, "2026-01-03", "2026-01-30");

            using var gate = new SemaphoreSlim(0);
            var credits = new[] { 60m, 70m }.Select(async amount => {
                                                        await gate.WaitAsync();
                                                        return await CreditAsync(client, id, amount, "2026-01-15");
                                                    }).ToList();
            gate.Release(credits.Count);
            await Task.WhenAll(credits);

            var (lines, allocations) = await StoredAsync(tenant, id);
            lines.Count.ShouldBe(5);
            ShouldNotOverAllocate(lines, allocations);
            allocations.Sum(a => a.Amount).ShouldBe(130m);
            ShouldMatchReadTimeFifo(await DueItemsAsync(client, id), await EntriesAsync(tenant, id));
        }
    }

    [Test]
    public async Task Backfill_CreatesDueLineForEveryExistingEntry() {
        var       tenant = Guid.CreateVersion7();
        using var client = await Client(tenant);
        var       id     = await CreateCardAsync(client);
        await DebitAsync(client, id, 100m, "2026-01-01", "2026-01-10");
        var cancelled = await DebitAsync(client, id, 50m, "2026-01-02", "2026-01-12");
        await DebitAsync(client, id, 200m, "2026-01-03", "2026-02-10");
        await CreditAsync(client, id, 120m, "2026-01-15");
        await AddAsync(client, id, new { type = "credit", date = "2026-01-20", amount = 30m, dueDate = "2026-03-01" });
        await CancelAsync(client, id, cancelled);

        using var otherClient = await Client(Guid.CreateVersion7());
        var       other       = await CreateCardAsync(otherClient);
        await DebitAsync(otherClient, other, 40m, "2026-01-01", "2026-01-05");
        await CreditAsync(otherClient, other, 15m, "2026-01-06");

        var expected = await DueItemsAsync(client, id);
        await ExecuteAsync("DELETE FROM ledger_allocations; DELETE FROM ledger_due_lines; "
                           + "DELETE FROM \"__modaryo_backfill\";");
        (await StoredAsync(tenant, id)).Lines.ShouldBeEmpty();

        var backfill = Factory.Services.GetRequiredService<StoredAllocationsBackfill>();
        (await backfill.RunAsync(CancellationToken.None)).ShouldBeTrue();
        (await backfill.RunAsync(CancellationToken.None)).ShouldBeFalse(); // işaret satırı: ikinci çalıştırma boş geçer

        var entries = await EntriesAsync(tenant, id);
        var (lines, allocations) = await StoredAsync(tenant, id);
        lines.Count.ShouldBe(entries.Count);
        entries.ShouldAllBe(e => lines.Count(l => l.EntryId == e.Id) == 1);
        allocations.ShouldAllBe(a => a.TenantId == tenant && a.Kind == AllocationKind.Auto);
        ShouldNotOverAllocate(lines, allocations);

        var actual = await DueItemsAsync(client, id);
        actual.Select(WithoutLineId).ToList().ShouldBe(expected.Select(WithoutLineId).ToList());
        ShouldMatchReadTimeFifo(actual, entries);

        var otherItem = (await DueItemsAsync(otherClient, other)).ShouldHaveSingleItem();
        otherItem.GetProperty("remaining").GetDecimal().ShouldBe(25m);
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
