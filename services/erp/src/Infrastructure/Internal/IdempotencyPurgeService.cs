using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Internal;

/// <summary>Eski idempotency kayıtlarını saatte bir temizler.</summary>
public sealed class IdempotencyPurgeService(
    IServiceScopeFactory               scopes,
    ILogger<IdempotencyPurgeService>   logger
) : BackgroundService {
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(Interval);
        do {
            try {
                await using var scope = scopes.CreateAsyncScope();
                var removed = await scope.ServiceProvider.GetRequiredService<IdempotencyStore>().PurgeAsync(stoppingToken);
                if (removed > 0) logger.LogInformation("{Count} eski idempotency kaydı silindi.", removed);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                logger.LogWarning(e, "Idempotency kayıtları temizlenemedi.");
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct) {
        try {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException) {
            return false;
        }
    }
}
