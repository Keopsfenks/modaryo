using ERP.Application.CurrentAccounts;
using ERP.Domain.Sales;
using ERP.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Sales;

/// <summary>
///     Günlük iş: geçerlilik tarihi geçmiş Gönderildi teklifleri Süresi Doldu durumuna alır. Firmalar arası çalışır ve
///     tekrar çalıştırılması güvenlidir (yalnızca hâlâ Gönderildi olanlar etkilenir).
/// </summary>
internal sealed class QuoteExpiryJob(
    RelayDatabase           database,
    TimeProvider            time,
    ILogger<QuoteExpiryJob> logger
) : BackgroundService {
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>Bir tur çalıştırır (testler çağırır). tenantId verilirse yalnızca o firma; today verilmezse İstanbul bugünü. Süresi dolan teklif sayısı döner.</summary>
    public async Task<int> PublicRunOnceAsync(CancellationToken ct, Guid? tenantId = null, DateOnly? today = null) {
        var day = today ?? CurrentAccountClock.Today(time);
        await using var db = database.Create();
        var query = db.Quotes.IgnoreQueryFilters()
                      .Where(q => q.Status == QuoteStatus.Sent
                                  && q.Revisions.Any(r => r.Revision == q.CurrentRevision && r.ValidUntil != null && r.ValidUntil < day));
        if (tenantId is { } only) query = query.Where(q => q.TenantId == only);

        var now = time.GetUtcNow();
        return await query.ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QuoteStatus.Expired).SetProperty(q => q.LastModified, now), ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(Interval);
        try {
            do {
                try {
                    var expired = await PublicRunOnceAsync(stoppingToken);
                    if (expired > 0) logger.LogInformation("{Count} teklifin süresi doldu.", expired);
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    logger.LogWarning(e, "Teklif süre sonu işi çalışamadı.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { }
    }
}
