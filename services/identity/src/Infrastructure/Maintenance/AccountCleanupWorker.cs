using IdentityService.Application.Maintenance;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdentityService.Infrastructure.Maintenance;

/// <summary>
/// Hesap temizliğini açılışta ve sonra 24 saatte bir çalıştırır. "Maintenance:CleanupEnabled=false" ile kapatılır.
/// </summary>
public sealed class AccountCleanupWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<AccountCleanupWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Maintenance:CleanupEnabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var report = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RunAccountCleanupCommand(), stoppingToken);
                logger.LogInformation("Hesap temizliği: {Unconfirmed} doğrulanmamış silindi, {Reminders} hatırlatma, {Companyless} firmasız silindi",
                    report.UnconfirmedDeleted, report.RemindersSent, report.CompanylessDeleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Hesap temizliği başarısız oldu.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
