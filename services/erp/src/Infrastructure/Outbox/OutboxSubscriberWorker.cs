using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory.Costing;
using ERP.Domain.Platform;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Outbox;

/// <summary>Süreç içi abonenin kaydı: ad (durum satırı anahtarı), hangi olay türlerini aldığı ve kapsamlı uygulama türü (IOutboxSubscriber).</summary>
public sealed record OutboxSubscriberDescriptor(string Name, Func<string, bool> Handles, Type Implementation);

/// <summary>
///     Süreç içi outbox abonelerini (IOutboxSubscriber, ör. müşteri sevk e-postası) çalıştıran ayrı döngü. Akışa iletimden (OutboxRelay) bağımsızdır: abone satırları iş
///     işleminde (Outbox.Enqueue) açılır, bu yüzden Valkey kesintisi e-postayı durdurmaz, yavaş bir SMTP sunucusu da olay iletimini geciktirmez.
///     Satırlar kısa bir işlemde kiralanır (FOR UPDATE SKIP LOCKED, sıradaki deneme kira süresi kadar ileri alınır); abone çağrısı kilit altında yapılmaz ve her çağrı
///     kira süresinden kısa bir zaman aşımına bağlıdır. Hatada üstel geri çekilme (30 sn'den başlar, en çok 4 saat) ile yaklaşık 24 saat boyunca denenir;
///     sınırda satır bırakılır (GaveUp) ve hata düzeyinde günlüğe yazılır (uyarı). At-least-once: abone tekrarlara dayanıklı olmalıdır.
/// </summary>
internal sealed class OutboxSubscriberWorker(
    RelayDatabase                            database,
    IServiceScopeFactory                     scopes,
    IOptions<OutboxOptions>                  options,
    TimeProvider                             time,
    ILogger<OutboxSubscriberWorker>          logger,
    IEnumerable<OutboxSubscriberDescriptor>  subscribers
) : BackgroundService {
    /// <summary>Abone bir olay için bu kadar denemeden sonra bırakılır (toplam bekleme ~24 saat).</summary>
    public const int MaxAttempts = 15;

    public static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(4);

    /// <summary>Tek abone çağrısı için üst süre; kira süresinden çok kısadır (yeniden talep edilip çift gönderim olmasın).</summary>
    public static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private const int BatchSize = 20;

    private const int Parallelism = 4;

    private const string DueSql
        = "SELECT * FROM outbox_subscriber_states WHERE \"CompletedAt\" IS NULL AND NOT \"GaveUp\" AND \"NextAttemptAt\" <= {0} ORDER BY \"NextAttemptAt\" LIMIT {1} FOR UPDATE SKIP LOCKED";

    private readonly IReadOnlyList<OutboxSubscriberDescriptor> _subscribers = subscribers.ToList();

    /// <summary>Hata sonrası bekleme: 30 sn, 1 dk, 2 dk ... en çok 4 saat (deneme sayısı 1'den başlar).</summary>
    public static TimeSpan Backoff(int attempts) {
        var seconds = BaseBackoff.TotalSeconds * Math.Pow(2, Math.Min(Math.Max(attempts, 1) - 1, 20));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
    }

    /// <summary>Vadesi gelen abone satırlarından bir parti işler; işlenen satır sayısını döndürür.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct) {
        if (_subscribers.Count == 0) return 0;

        List<(Guid MessageId, string Subscriber)> claimed;
        await using (var db = database.Create()) {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var now  = time.GetUtcNow();
            var rows = await db.OutboxSubscriberStates.FromSqlRaw(DueSql, now, BatchSize).IgnoreQueryFilters().ToListAsync(ct);
            foreach (var row in rows) row.NextAttemptAt = now + Lease;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            claimed = rows.Select(r => (r.MessageId, r.Subscriber)).ToList();
        }

        await Parallel.ForEachAsync(claimed, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
                                    async (item, token) => await ProcessAsync(item.MessageId, item.Subscriber, token));
        return claimed.Count;
    }

    private async Task ProcessAsync(Guid messageId, string subscriberName, CancellationToken ct) {
        var        descriptor = _subscribers.FirstOrDefault(s => s.Name == subscriberName);
        Exception? error      = null;
        var        attempted  = false;
        var        tenantId   = Guid.Empty;
        try {
            if (descriptor is null) throw new InvalidOperationException($"Abone \"{subscriberName}\" kayıtlı değil.");

            await using var readDb  = database.Create();
            var             message = await readDb.OutboxMessages.IgnoreQueryFilters().AsNoTracking().FirstAsync(m => m.Id == messageId, ct);
            tenantId = message.TenantId;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(HandlerTimeout);
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantScope>().Override = new BackgroundTenant(message.TenantId);
            var handler = (IOutboxSubscriber)scope.ServiceProvider.GetRequiredService(descriptor.Implementation);
            attempted = await handler.HandleAsync(new OutboxEvent(message.Id, message.Type, message.PayloadJson, message.TenantId, message.OccurredAt), timeout.Token);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested) {
            error = e is OperationCanceledException ? new TimeoutException($"Abone {HandlerTimeout.TotalSeconds:0} sn içinde tamamlanamadı.", e) : e;
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
            if (state.Attempts >= MaxAttempts) {
                state.GaveUp = true;
                logger.LogError(error, "Abone {Subscriber} olay {MessageId} için {Attempts} denemeden sonra bırakıldı (firma {Tenant}); elle müdahale gerekir (OutboxSubscriberStates.GaveUp).",
                                subscriberName, messageId, state.Attempts, tenantId);
            }
            else {
                logger.LogWarning(error, "Abone {Subscriber} olay {MessageId} için başarısız (deneme {Attempts}); {Next:u} sonra yeniden denenecek.", subscriberName, messageId,
                                  state.Attempts, state.NextAttemptAt);
            }
        }

        await write.SaveChangesAsync(CancellationToken.None);
    }

    private static string Truncate(string error) {
        return error.Length <= OutboxOptions.MaxLastErrorLength ? error : error[..OutboxOptions.MaxLastErrorLength];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var poll = TimeSpan.FromMilliseconds(Math.Max(options.Value.PollIntervalMs, 10));
        while (!stoppingToken.IsCancellationRequested) {
            var delay = poll;
            try {
                // Dolu parti: bekleme yapmadan devam et.
                if (await RunOnceAsync(stoppingToken) == BatchSize) delay = TimeSpan.Zero;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception e) {
                delay = OutboxRelay.MaxBackoff;
                logger.LogWarning(e, "Outbox abone döngüsü çalışamadı; {Delay} sonra yeniden denenecek.", delay);
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
