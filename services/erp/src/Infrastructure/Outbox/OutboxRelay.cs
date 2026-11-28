using System.Globalization;
using System.Text.Json.Nodes;
using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory.Costing;
using ERP.Domain.Platform;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ERP.Infrastructure.Outbox;

/// <summary>
///     Röle firma bağlamı olmadan çalışır: kendi DbContext'ini firma denetleyicisi (interceptor) olmadan kurar.
///     Geri doldurma gibi firmalar arası arka plan işleri de bu bağlamları kullanır.
/// </summary>
internal sealed class RelayDatabase(
    DbContextOptions<ApplicationDbContext> options
) {
    public RelayDatabase(string connectionString)
        : this(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options) { }

    public ApplicationDbContext Create() {
        return new ApplicationDbContext(options, NoTenant.Instance);
    }

    /// <summary>Sorguları tek firmaya filtrelenen bağlam. Denetleyici yoktur: yeni satırlara TenantId açıkça yazılmalıdır.</summary>
    public ApplicationDbContext CreateForTenant(Guid tenantId) {
        return new ApplicationDbContext(options, new SingleTenant(tenantId));
    }

    private sealed class SingleTenant(
        Guid tenantId
    ) : ICurrentTenant {
        public Guid? TenantId => tenantId;

        public Guid? UserId => null;

        public string? UserFullName => null;

        public IReadOnlyList<Guid>? BranchIds => [];

        public decimal MaxDiscountPercent => 0;

        public bool HasModule(string key) {
            return false;
        }

        public bool CanAccessBranch(Guid branchId) {
            return false;
        }
    }

    private sealed class NoTenant : ICurrentTenant {
        public static readonly NoTenant Instance = new();

        public Guid? TenantId => null;

        public Guid? UserId => null;

        public string? UserFullName => null;

        public IReadOnlyList<Guid>? BranchIds => [];

        public decimal MaxDiscountPercent => 0;

        public bool HasModule(string key) {
            return false;
        }

        public bool CanAccessBranch(Guid branchId) {
            return false;
        }
    }
}

/// <summary>Süreç içi abonenin kaydı: ad (durum satırı anahtarı), hangi olay türlerini aldığı ve kapsamlı uygulama türü (IOutboxSubscriber).</summary>
internal sealed record OutboxSubscriberDescriptor(string Name, Func<string, bool> Handles, Type Implementation);

/// <summary>
///     Bekleyen outbox kayıtlarını (Id sırasıyla, en çok 100) "erp.events" akışına XADD ile iletir.
///     At-least-once: XADD ile PublishedAt yazımı arasında süreç ölürse kayıt yeniden iletilebilir;
///     tüketiciler zarf "id" alanıyla tekilleştirir. Çoklu röle örneği FOR UPDATE SKIP LOCKED ile çakışmaz.
/// </summary>
internal sealed class OutboxRelay(
    RelayDatabase           database,
    IEventStreamPublisher   publisher,
    IOptions<OutboxOptions> options,
    TimeProvider            time,
    ILogger<OutboxRelay>    logger,
    IEnumerable<OutboxSubscriberDescriptor>? subscribers = null,
    IServiceScopeFactory?   scopes = null
) : BackgroundService {
    /// <summary>Abone bir olay için bu kadar denemeden sonra bırakılır (günlüğe hata yazılır).</summary>
    public const int MaxSubscriberAttempts = 6;

    private static readonly TimeSpan SubscriberLease = TimeSpan.FromMinutes(2);

    private readonly IReadOnlyList<OutboxSubscriberDescriptor> _subscribers = (subscribers ?? []).ToList();

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(1);

    private const string PendingSql
        = "SELECT * FROM outbox_messages WHERE \"PublishedAt\" IS NULL ORDER BY \"Id\" LIMIT 100 FOR UPDATE SKIP LOCKED";

    /// <summary>İletilen kayıt sayısını döndürür. Akış hatasında kalan kayıtlar bekler (istisna fırlatmaz).</summary>
    public async Task<int> PublishPendingAsync(CancellationToken ct) {
        var published = (await PublishBatchAsync(ct)).Published;
        await RunSubscribersAsync(ct);
        return published;
    }

    private async Task<(int Published, bool Failed)> PublishBatchAsync(CancellationToken ct) {
        await using var db          = database.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var pending = await db.OutboxMessages.FromSqlRaw(PendingSql).IgnoreQueryFilters().ToListAsync(ct);

        var published = 0;
        var failed    = false;
        foreach (var message in pending) {
            try {
                await publisher.PublishAsync(OutboxOptions.StreamKey, ToFields(message), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested) {
                message.Attempts++;
                message.LastError = Truncate(e.Message);
                failed            = true;
                logger.LogWarning(e, "Olay {MessageId} akışa iletilemedi (deneme {Attempts}).", message.Id,
                                  message.Attempts);
                break;
            }

            message.PublishedAt = time.GetUtcNow();
            published++;

            // Aboneler akışa iletimle aynı işlemde kayda geçer: olay iletildiyse abone satırı da vardır (ve tersi).
            foreach (var subscriber in _subscribers.Where(s => s.Handles(message.Type)))
                db.OutboxSubscriberStates.Add(new OutboxSubscriberState {
                                                                            MessageId = message.Id, Subscriber = subscriber.Name, TenantId = message.TenantId,
                                                                            NextAttemptAt = message.PublishedAt.Value
                                                                        });
        }

        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return (published, failed);
    }

    private const string DueSql
        = "SELECT * FROM outbox_subscriber_states WHERE \"CompletedAt\" IS NULL AND NOT \"GaveUp\" AND \"NextAttemptAt\" <= {0} ORDER BY \"NextAttemptAt\" LIMIT 50 FOR UPDATE SKIP LOCKED";

    /// <summary>
    ///     Vadesi gelen abone satırlarını işler. Satırlar kısa bir işlemde kiralanır (sıradaki deneme ileri alınır) ve kilitler hemen bırakılır;
    ///     abone çağrısı kilit altında yapılmaz. Hata abonenin satırına yazılır; iş verisine ve akışa dokunmaz.
    /// </summary>
    internal async Task<int> RunSubscribersAsync(CancellationToken ct) {
        if (_subscribers.Count == 0 || scopes is null) return 0;

        List<(Guid MessageId, string Subscriber)> claimed;
        await using (var db = database.Create()) {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var now  = time.GetUtcNow();
            var rows = await db.OutboxSubscriberStates.FromSqlRaw(DueSql, now).IgnoreQueryFilters().ToListAsync(ct);
            foreach (var row in rows) row.NextAttemptAt = now + SubscriberLease;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            claimed = rows.Select(r => (r.MessageId, r.Subscriber)).ToList();
        }

        foreach (var (messageId, subscriberName) in claimed) {
            var       descriptor = _subscribers.FirstOrDefault(s => s.Name == subscriberName);
            Exception? error     = null;
            var       attempted  = false;
            var       tenantId   = Guid.Empty;
            try {
                if (descriptor is null) throw new InvalidOperationException($"Abone \"{subscriberName}\" kayıtlı değil.");

                await using var readDb  = database.Create();
                var             message = await readDb.OutboxMessages.IgnoreQueryFilters().AsNoTracking().FirstAsync(m => m.Id == messageId, ct);
                tenantId = message.TenantId;

                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantScope>().Override = new BackgroundTenant(message.TenantId);
                var handler = (IOutboxSubscriber)scope.ServiceProvider.GetRequiredService(descriptor.Implementation);
                attempted = await handler.HandleAsync(new OutboxEvent(message.Id, message.Type, message.PayloadJson, message.TenantId, message.OccurredAt), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested) {
                error = e;
            }

            await using var write = database.Create();
            var state = await write.OutboxSubscriberStates.IgnoreQueryFilters()
                                   .FirstAsync(s => s.MessageId == messageId && s.Subscriber == subscriberName, CancellationToken.None);
            var at = time.GetUtcNow();
            if (error is null) {
                state.CompletedAt = at;
                state.LastError   = null;
                if (attempted) state.Attempts++;
            }
            else {
                state.Attempts++;
                state.LastError     = Truncate(error.Message);
                state.NextAttemptAt = at + Backoff(state.Attempts);
                if (state.Attempts >= MaxSubscriberAttempts) {
                    state.GaveUp = true;
                    logger.LogError(error, "Abone {Subscriber} olay {MessageId} için {Attempts} denemeden sonra bırakıldı (firma {Tenant}).", subscriberName, messageId,
                                    state.Attempts, tenantId);
                }
                else {
                    logger.LogWarning(error, "Abone {Subscriber} olay {MessageId} için başarısız (deneme {Attempts}); {Next:u} sonra yeniden denenecek.", subscriberName, messageId,
                                      state.Attempts, state.NextAttemptAt);
                }
            }

            await write.SaveChangesAsync(CancellationToken.None);
        }

        return claimed.Count;
    }

    private static string Truncate(string error) {
        return error.Length <= OutboxOptions.MaxLastErrorLength
                   ? error
                   : error[..OutboxOptions.MaxLastErrorLength];
    }

    public static NameValueEntry[] ToFields(OutboxMessage message) {
        var envelope = new JsonObject {
                                          ["v"]           = 1,
                                          ["id"]          = message.Id.ToString(),
                                          ["type"]        = message.Type,
                                          ["tenant_id"]   = message.TenantId.ToString(),
                                          ["occurred_at"] = FormatTime(message.OccurredAt),
                                          ["actor"]       = message.Actor,
                                          ["data"]        = JsonNode.Parse(message.PayloadJson)
                                      };
        return [
                   new NameValueEntry("type", message.Type),
                   new NameValueEntry("tenant_id", message.TenantId.ToString()),
                   new NameValueEntry("event", envelope.ToJsonString())
               ];
    }

    public static string FormatTime(DateTimeOffset value) {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>Hata sonrası bekleme: 1 sn, 2 sn, 4 sn ... en çok 60 sn.</summary>
    public static TimeSpan Backoff(int consecutiveFailures) {
        var seconds = BaseBackoff.TotalSeconds * Math.Pow(2, Math.Min(consecutiveFailures - 1, 10));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var failures = 0;
        var poll     = TimeSpan.FromMilliseconds(Math.Max(options.Value.PollIntervalMs, 10));
        while (!stoppingToken.IsCancellationRequested) {
            var delay = poll;
            try {
                var (published, failed) = await PublishBatchAsync(stoppingToken);
                await RunSubscribersAsync(stoppingToken);
                if (failed) {
                    failures++;
                    delay = Backoff(failures);
                }
                else {
                    failures = 0;
                    // Dolu parti: bekleme yapmadan devam et.
                    if (published == OutboxOptions.BatchSize) delay = TimeSpan.Zero;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception e) {
                failures++;
                delay = Backoff(failures);
                logger.LogWarning(e, "Outbox rölesi çalışamadı; {Delay} sonra yeniden denenecek.", delay);
            }

            try {
                if (delay > TimeSpan.Zero) await Task.Delay(delay, time, stoppingToken);
            }
            catch (OperationCanceledException) {
                break;
            }
        }
    }
}
