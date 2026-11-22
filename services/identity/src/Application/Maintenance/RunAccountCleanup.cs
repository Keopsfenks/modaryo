using IdentityService.Application.Common.Interfaces;

namespace IdentityService.Application.Maintenance;

public sealed record CleanupReport(int UnconfirmedDeleted, int RemindersSent, int CompanylessDeleted);

/// <summary>
/// Hesap temizliği (spec 7.6). Arka plan işi günde bir çalıştırır; HTTP uç noktası yoktur.
/// <list type="bullet">
/// <item>E-postası 7 gün içinde doğrulanmamış hesaplar silinir.</item>
/// <item>Firmasız kullanıcı: sayaç max(EmailConfirmedAt, LastMembershipEndedAt). 23. gün bir kez hatırlatma, 30. gün silme.</item>
/// </list>
/// </summary>
public record RunAccountCleanupCommand : IRequest<CleanupReport>;

public class RunAccountCleanupCommandHandler(
    IUserAccounts users,
    ISessionStore sessions,
    IEmailComposer composer,
    IEmailSender sender,
    TimeProvider time)
    : IRequestHandler<RunAccountCleanupCommand, CleanupReport>
{
    public static readonly TimeSpan UnconfirmedLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan ReminderAfter = TimeSpan.FromDays(23);
    public static readonly TimeSpan DeleteAfter = TimeSpan.FromDays(30);

    public async Task<CleanupReport> Handle(RunAccountCleanupCommand request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        int unconfirmedDeleted = 0, remindersSent = 0, companylessDeleted = 0;

        foreach (var user in await users.ListCleanupCandidatesAsync(ct))
        {
            if (!user.EmailConfirmed)
            {
                if (now - user.CreatedAt >= UnconfirmedLifetime)
                {
                    await DeleteAsync(user, ct);
                    unconfirmedDeleted++;
                }

                continue;
            }

            var clockStart = Max(user.EmailConfirmedAt ?? user.CreatedAt, user.LastMembershipEndedAt);

            if (now - clockStart >= DeleteAfter)
            {
                await DeleteAsync(user, ct);
                companylessDeleted++;
            }
            else if (now - clockStart >= ReminderAfter && (user.CleanupReminderSentAt is null || user.CleanupReminderSentAt < clockStart))
            {
                await sender.SendAsync(composer.CleanupReminder(user.Email, user.FullName, clockStart.Add(DeleteAfter)), ct);
                await users.SetCleanupReminderSentAsync(user.Id, now);
                remindersSent++;
            }
        }

        return new CleanupReport(unconfirmedDeleted, remindersSent, companylessDeleted);
    }

    private async Task DeleteAsync(UserSummary user, CancellationToken ct)
    {
        await sessions.DeleteAllForUserAsync(user.Id, exceptSidHash: null, ct);
        await users.DeleteAsync(user.Id);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset? b) => b is { } value && value > a ? value : a;
}
