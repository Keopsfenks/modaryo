using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Platform;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ERP.Web.FunctionalTests.Platform;

public class OutboxTests : TestBase {
    private const           string Url    = "/api/erp/current-accounts";
    private static readonly Guid   Tenant = Guid.CreateVersion7();

    private static OutboxRelay Relay => Factory.Services.GetRequiredService<OutboxRelay>();

    private static IDatabase Valkey => Factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    private static Task<HttpClient> Client() {
        return Factory.ClientAsync(Tenant, "cari.read", "cari.manage", "cari.movements");
    }

    private static async Task<Guid> CreateCardAsync(HttpClient client, object? opening = null) {
        using var response = await client.PostAsJsonAsync(Url, new {
                                                                       code           = "C0001",
                                                                       name           = "Örnek Ltd.",
                                                                       kind           = "corporate",
                                                                       isCustomer     = true,
                                                                       currency       = "TRY",
                                                                       defaultDueDays = 30,
                                                                       openingBalance = opening
                                                                   });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task AddDebitAsync(HttpClient client, Guid id, decimal amount, string? description = null) {
        using var response = await client.PostAsJsonAsync($"{Url}/{id}/entries", new {
                                                              type = "debit", date = "2026-11-01", amount,
                                                              description
                                                          });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<List<OutboxMessage>> MessagesAsync() {
        await using var scope = Factory.Services.CreateAsyncScope();
        var             db    = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.OutboxMessages.IgnoreQueryFilters().AsNoTracking().OrderBy(m => m.Id).ToListAsync();
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

    [Test]
    public async Task AddEntry_EnqueuesEntryAddedEventWithPayload() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client);

        await AddDebitAsync(client, id, 250.5m, "Satış");

        var message = (await MessagesAsync()).ShouldHaveSingleItem();
        message.Type.ShouldBe("current-account.entry-added");
        message.TenantId.ShouldBe(Tenant);
        message.Actor.ShouldBe("Ali Veli");
        message.PublishedAt.ShouldBeNull();
        using var data = JsonDocument.Parse(message.PayloadJson);
        var       root = data.RootElement;
        root.GetProperty("current_account_id").GetGuid().ShouldBe(id);
        root.GetProperty("entry_type").GetString().ShouldBe("debit-note");
        root.GetProperty("kind").GetString().ShouldBe("debit");
        root.GetProperty("amount").GetString().ShouldBe("250.50");
        root.GetProperty("currency").GetString().ShouldBe("TRY");
        root.GetProperty("date").GetString().ShouldBe("2026-11-01");
        root.GetProperty("due_date").GetString().ShouldBe("2026-12-01");
        root.GetProperty("description").GetString().ShouldBe("Satış");
    }

    [Test]
    public async Task AddEntry_EnqueuesOutboxMessageInSameTransaction() {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(TestEnvironment.ConnectionString)
            .Options;
        var tenant = new FixedTenant(Tenant);

        await using (var db = new ApplicationDbContext(options, tenant)) {
            new Outbox(db, tenant, TimeProvider.System).Enqueue("test.event", new { a = 1 });
            // Aynı kod iki kez: benzersiz indeks SaveChanges'i, Enqueue'dan sonra ama kayıttan önce bozar.
            db.CurrentAccounts.Add(NewAccount());
            db.CurrentAccounts.Add(NewAccount());
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        (await MessagesAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task AddCredit_EmitsCreditNoteEntryType() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client);
        (await client.PostAsJsonAsync($"{Url}/{id}/entries", new { type = "credit", date = "2026-11-01", amount = 5m }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        using var data = JsonDocument.Parse((await MessagesAsync()).Single().PayloadJson);
        data.RootElement.GetProperty("entry_type").GetString().ShouldBe("credit-note");
        data.RootElement.GetProperty("kind").GetString().ShouldBe("credit");
    }

    private sealed class FailingPublisher : IEventStreamPublisher {
        public int Calls { get; private set; }

        public Task PublishAsync(string streamKey, NameValueEntry[] fields, CancellationToken ct) {
            Calls++;
            throw new InvalidOperationException("akış kapalı");
        }
    }

    [Test]
    public async Task PublishFailure_StopsBatchAndKeepsLaterMessagesUntouched() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client);
        await AddDebitAsync(client, id, 1m);
        await AddDebitAsync(client, id, 2m);
        var publisher = new FailingPublisher();
        var relay = new OutboxRelay(new RelayDatabase(TestEnvironment.ConnectionString), publisher,
                                    Options.Create(new OutboxOptions()), TimeProvider.System,
                                    NullLogger<OutboxRelay>.Instance);

        (await relay.PublishPendingAsync(CancellationToken.None)).ShouldBe(0);

        publisher.Calls.ShouldBe(1);
        var messages = await MessagesAsync();
        messages.ShouldAllBe(m => m.PublishedAt == null);
        messages[0].Attempts.ShouldBe(1);
        messages[0].LastError.ShouldBe("akış kapalı");
        messages[1].Attempts.ShouldBe(0);
    }

    [Test]
    public async Task CardWithOpening_EnqueuesOneEventForOpeningEntry() {
        using var client = await Client();

        var id = await CreateCardAsync(client, new { amount = 100m, direction = "credit", date = "2026-01-01" });

        var message = (await MessagesAsync()).ShouldHaveSingleItem();
        using var data = JsonDocument.Parse(message.PayloadJson);
        data.RootElement.GetProperty("current_account_id").GetGuid().ShouldBe(id);
        data.RootElement.GetProperty("entry_type").GetString().ShouldBe("opening");
        data.RootElement.GetProperty("kind").GetString().ShouldBe("credit");
        data.RootElement.GetProperty("due_date").ValueKind.ShouldBe(JsonValueKind.Null);
        data.RootElement.GetProperty("description").GetString().ShouldBe("Açılış bakiyesi");
    }

    [Test]
    public async Task PublishPending_WritesEnvelopeToStream() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client);
        await AddDebitAsync(client, id, 99.9m);
        var messageId = (await MessagesAsync()).Single().Id;

        var count = await Relay.PublishPendingAsync(CancellationToken.None);

        count.ShouldBe(1);
        var entries = await Valkey.StreamRangeAsync("erp.events");
        var entry   = entries.ShouldHaveSingleItem();
        entry.Values.Single(v => v.Name == "type").Value.ToString().ShouldBe("current-account.entry-added");
        entry.Values.Single(v => v.Name == "tenant_id").Value.ToString().ShouldBe(Tenant.ToString());
        using var envelope = JsonDocument.Parse(entry.Values.Single(v => v.Name == "event").Value.ToString());
        var       root     = envelope.RootElement;
        root.GetProperty("v").GetInt32().ShouldBe(1);
        root.GetProperty("id").GetGuid().ShouldBe(messageId);
        root.GetProperty("type").GetString().ShouldBe("current-account.entry-added");
        root.GetProperty("tenant_id").GetGuid().ShouldBe(Tenant);
        root.GetProperty("actor").GetString().ShouldBe("Ali Veli");
        var occurred = DateTimeOffset.Parse(root.GetProperty("occurred_at").GetString()!);
        occurred.ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        root.GetProperty("occurred_at").GetString().ShouldEndWith("Z");
        var data = root.GetProperty("data");
        data.GetProperty("current_account_id").GetGuid().ShouldBe(id);
        data.GetProperty("amount").GetString().ShouldBe("99.90");

        (await MessagesAsync()).Single().PublishedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task PublishPending_IsIdempotentForPublishedMessages() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client);
        await AddDebitAsync(client, id, 10m);
        await AddDebitAsync(client, id, 20m);

        (await Relay.PublishPendingAsync(CancellationToken.None)).ShouldBe(2);
        (await Relay.PublishPendingAsync(CancellationToken.None)).ShouldBe(0);

        (await Valkey.StreamRangeAsync("erp.events")).Length.ShouldBe(2);
    }

    [Test]
    public async Task PublishPending_PreservesOrder() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client);
        foreach (var amount in new[] { 1m, 2m, 3m }) await AddDebitAsync(client, id, amount);

        await Relay.PublishPendingAsync(CancellationToken.None);

        var amounts = (await Valkey.StreamRangeAsync("erp.events"))
            .Select(e => JsonDocument.Parse(e.Values.Single(v => v.Name == "event").Value.ToString()).RootElement
                         .GetProperty("data").GetProperty("amount").GetString());
        amounts.ShouldBe(["1.00", "2.00", "3.00"]);
    }

    [Test]
    public async Task ValkeyDown_KeepsMessagesPending() {
        using var client = await Client();
        var       id     = await CreateCardAsync(client);
        await AddDebitAsync(client, id, 10m);

        await using var closed = await ConnectionMultiplexer.ConnectAsync(
                                     "localhost:1,abortConnect=false,connectTimeout=300,syncTimeout=300,asyncTimeout=300");
        var relay = new OutboxRelay(new RelayDatabase(TestEnvironment.ConnectionString),
                                    new ValkeyEventStreamPublisher(closed), Options.Create(new OutboxOptions()),
                                    TimeProvider.System, NullLogger<OutboxRelay>.Instance);

        (await relay.PublishPendingAsync(CancellationToken.None)).ShouldBe(0);

        var message = (await MessagesAsync()).Single();
        message.PublishedAt.ShouldBeNull();
        message.Attempts.ShouldBe(1);
        message.LastError.ShouldNotBeNullOrEmpty();

        // Valkey geri gelince aynı kayıt iletilir.
        (await Relay.PublishPendingAsync(CancellationToken.None)).ShouldBe(1);
        (await MessagesAsync()).Single().PublishedAt.ShouldNotBeNull();
    }

    [Test]
    public void Backoff_DoublesFromOneSecondAndCapsAtSixtySeconds() {
        OutboxRelay.Backoff(1).ShouldBe(TimeSpan.FromSeconds(1));
        OutboxRelay.Backoff(2).ShouldBe(TimeSpan.FromSeconds(2));
        OutboxRelay.Backoff(3).ShouldBe(TimeSpan.FromSeconds(4));
        OutboxRelay.Backoff(7).ShouldBe(TimeSpan.FromSeconds(60));
        OutboxRelay.Backoff(1000).ShouldBe(TimeSpan.FromSeconds(60));
    }

    private static CurrentAccount NewAccount() {
        return new CurrentAccount {
                                      TenantId   = Tenant,
                                      Code       = "DUP",
                                      Name       = "Yinelenen",
                                      IsCustomer = true,
                                      Currency   = "TRY"
                                  };
    }
}
