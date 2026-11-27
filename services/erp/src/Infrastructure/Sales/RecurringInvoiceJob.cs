using ERP.Application.CurrentAccounts;
using ERP.Application.Inventory.Costing;
using ERP.Application.Sales.Recurring;
using ERP.Domain.Sales;
using ERP.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Sales;

/// <summary>
///     Günlük iş (06:00 İstanbul): bugüne kadar üretilmemiş tekrarlayan fatura dönemlerinin faturalarını oluşturur. Firmalar arası çalışır; her (şablon, dönem)
///     denemesi kendi DI kapsamı ve firma bağlamıyla ayrı bir işlemde yürür, biri başarısız olursa diğerleri etkilenmez ve sonraki tur yeniden dener.
///     Yarışa dayanıklıdır: <see cref="RecurringGenerator" /> şablon satırını kilitleyip dönemi kilit altında yeniden denetler (aynı dönem iki kez üretilmez).
///     Kaçan günler (iş çalışmadıysa) sonraki turda yakalanır: bugüne kadarki tüm üretilmemiş dönemler, şablon başına en çok <see cref="MaxPeriodsPerTemplate" /> dönem ve en eskiden başlayarak üretilir.
/// </summary>
internal sealed class RecurringInvoiceJob(
    RelayDatabase                    database,
    IServiceScopeFactory             scopes,
    TimeProvider                     time,
    ILogger<RecurringInvoiceJob>     logger
) : BackgroundService {
    public const int MaxPeriodsPerTemplate = 12;

    private static readonly TimeSpan RunAt = new(6, 0, 0);

    /// <summary>Bir tur çalıştırır (testler çağırır). tenantId verilirse yalnızca o firma. Üretilen fatura sayısı döner.</summary>
    public async Task<int> RunOnceAsync(DateOnly today, Guid? tenantId = null, CancellationToken ct = default) {
        List<(Guid Id, Guid TenantId, List<DateOnly> Periods)> pending;
        await using (var db = database.Create()) {
            var query = db.RecurringTemplates.IgnoreQueryFilters().AsNoTracking().Where(t => t.IsActive && t.StartDate <= today);
            if (tenantId is { } only) query = query.Where(t => t.TenantId == only);

            var templates = await query.Select(t => new { t.Id, t.TenantId, t.Interval, t.DayOfMonth, t.StartDate, t.EndDate }).ToListAsync(ct);
            var ids       = templates.Select(t => t.Id).ToList();
            var done      = (await db.RecurringRuns.IgnoreQueryFilters().AsNoTracking().Where(r => ids.Contains(r.RecurringTemplateId)).Select(r => new { r.RecurringTemplateId, r.Period }).ToListAsync(ct))
                            .GroupBy(r => r.RecurringTemplateId).ToDictionary(g => g.Key, g => g.Select(r => r.Period).ToHashSet());
            pending = templates.Select(t => (t.Id, t.TenantId,
                                             RecurrenceCalendar.DueDates(t.Interval, t.DayOfMonth, t.StartDate, t.EndDate, today)
                                                               .Where(d => !(done.GetValueOrDefault(t.Id)?.Contains(d) ?? false)).Take(MaxPeriodsPerTemplate).ToList()))
                               .Where(t => t.Item3.Count > 0).ToList();
        }

        var generated = 0;
        foreach (var (id, tenant, periods) in pending) {
            foreach (var period in periods) {
                try {
                    await using var scope = scopes.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<TenantScope>().Override = new SalesBackgroundTenant(tenant);
                    if (await scope.ServiceProvider.GetRequiredService<RecurringGenerator>().GenerateAsync(id, period, ct) is not null) generated++;
                } catch (Exception e) when (e is not OperationCanceledException) {
                    // Dönemler sırayla üretilir: biri başarısızsa sonrakini atlayıp araya boşluk bırakmamak için bu şablon bir sonraki tura kalır.
                    logger.LogWarning(e, "Tekrarlayan fatura üretilemedi: şablon {Template}, dönem {Period}.", id, period);
                    break;
                }
            }
        }

        return generated;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            while (!stoppingToken.IsCancellationRequested) {
                var now  = time.GetUtcNow().UtcDateTime.AddHours(3); // İstanbul
                var next = now.Date + RunAt;
                if (next <= now) next = next.AddDays(1);

                await Task.Delay(next - now, stoppingToken);
                try {
                    var count = await RunOnceAsync(CurrentAccountClock.Today(time), null, stoppingToken);
                    if (count > 0) logger.LogInformation("{Count} tekrarlayan fatura üretildi.", count);
                } catch (Exception e) when (e is not OperationCanceledException) {
                    logger.LogWarning(e, "Tekrarlayan fatura işi çalışamadı.");
                }
            }
        } catch (OperationCanceledException) { }
    }
}
