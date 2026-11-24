using ERP.Application.Common.Interfaces;
using ERP.Application.Rates;
using ERP.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Rates;

/// <summary>
///     Açılışta son 10 günün eksik kurlarını, sonra her gün 15:45'te (İstanbul) TCMB bültenini alır.
///     Ağ hataları günlüğe yazılır, sonraki turda yeniden denenir; açılışı asla düşürmez.
/// </summary>
internal sealed class TcmbRateFetcher(
    RelayDatabase            database,
    IServiceScopeFactory     scopes,
    TimeProvider             clock,
    ILogger<TcmbRateFetcher> logger
) : BackgroundService {
    public static readonly TimeOnly DailyAt = new(15, 45);

    // Türkiye kalıcı UTC+3'tür (yaz saati uygulaması yok).
    private static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    /// <summary>Bir tur çalıştırır (testler çağırır). Kaydedilen satır sayısı döner.</summary>
    public async Task<int> PublicRunOnceAsync(CancellationToken ct) {
        await using var db     = database.Create();
        await using var scope  = scopes.CreateAsyncScope();
        var             source = scope.ServiceProvider.GetRequiredService<ITcmbRateSource>();
        var             result = await RateImport.ImportAsync(db, source, RateRules.Today(clock), logger, ct);
        return result.Stored;
    }

    /// <summary>Bir sonraki 15:45'e kalan süre.</summary>
    public static TimeSpan UntilNext(DateTimeOffset now) {
        var local = now.ToOffset(Offset);
        var next  = local.Date.Add(DailyAt.ToTimeSpan());
        if (next <= local.DateTime) next = next.AddDays(1);
        return next - local.DateTime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            await TryRunAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested) {
                await Task.Delay(UntilNext(clock.GetUtcNow()), stoppingToken);
                await TryRunAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task TryRunAsync(CancellationToken ct) {
        try {
            await PublicRunOnceAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            logger.LogWarning(e, "TCMB kur turu çalışamadı; sonraki turda yeniden denenecek.");
        }
    }
}
