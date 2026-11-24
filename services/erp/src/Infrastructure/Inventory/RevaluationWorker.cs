using ERP.Application.Inventory.Costing;
using ERP.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Inventory;

/// <summary>
///     Bekleyen yeniden değerleme işlerini 5 sn'de bir tarar. Firmalar arası çalışır: işi olan her firma için o firmanın
///     bağlamıyla bir kapsam açar ve <see cref="IRevaluationRunner" /> çalıştırır.
/// </summary>
internal sealed class RevaluationWorker(
    RelayDatabase                database,
    IServiceScopeFactory         scopes,
    ILogger<RevaluationWorker>   logger
) : BackgroundService {
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>Bir tur çalıştırır (testler çağırır). tenantId verilirse yalnızca o firmanın işleri işlenir. İşlenen iş sayısı döner.</summary>
    public async Task<int> PublicRunOnceAsync(CancellationToken ct, Guid? tenantId = null) {
        List<Guid> tenants;
        await using (var db = database.Create()) {
            var query = db.RevaluationJobs.IgnoreQueryFilters().AsNoTracking();
            if (tenantId is { } only) query = query.Where(j => j.TenantId == only);

            tenants = await query.Select(j => j.TenantId).Distinct().ToListAsync(ct);
        }

        var processed = 0;
        foreach (var tenant in tenants) {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantScope>().Override = new BackgroundTenant(tenant);
            processed += await scope.ServiceProvider.GetRequiredService<IRevaluationRunner>().RunPendingAsync(ct);
        }

        return processed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(Interval);
        try {
            do {
                try {
                    await PublicRunOnceAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    logger.LogWarning(e, "Yeniden değerleme turu çalışamadı.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { }
    }
}
