namespace IdentityService.Application.Common.Interfaces;

public sealed record UserSummary(
    Guid Id,
    string Email,
    string FullName,
    bool EmailConfirmed,
    Guid? LastActiveTenantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EmailConfirmedAt,
    DateTimeOffset? LastMembershipEndedAt,
    DateTimeOffset? CleanupReminderSentAt);

public enum PasswordCheckResult
{
    Success,
    InvalidCredentials,
    NotConfirmed,
    LockedOut,
}

public sealed record PasswordCheck(PasswordCheckResult Result, UserSummary? User, DateTimeOffset? LockoutEnd);

/// <summary>
/// ASP.NET Identity (UserManager) sarmalayıcısı. E-postalar her zaman kırpılıp küçük harfe çevrilerek saklanır/aranır.
/// </summary>
public interface IUserAccounts
{
    Task<UserSummary?> FindByEmailAsync(string email, CancellationToken ct);

    Task<UserSummary?> FindByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken ct);

    /// <summary>Şifre kurallarına uymazsa ValidationException fırlatır.</summary>
    Task<UserSummary> CreateAsync(string email, string fullName, string password, bool emailConfirmed, CancellationToken ct);

    Task<string> CreateEmailConfirmationTokenAsync(Guid userId);

    /// <summary>Başarılıysa EmailConfirmedAt yazılır.</summary>
    Task<bool> ConfirmEmailAsync(Guid userId, string token);

    Task MarkEmailConfirmedAsync(Guid userId);

    /// <summary>Kilit sayacını işletir (5 başarısız deneme → 15 dk).</summary>
    Task<PasswordCheck> CheckPasswordAsync(string email, string password);

    Task<string> CreatePasswordResetTokenAsync(Guid userId);

    Task<bool> ResetPasswordAsync(string email, string token, string newPassword);

    /// <summary>Mevcut şifre yanlışsa false; yeni şifre kurallara uymazsa ValidationException.</summary>
    Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);

    Task SetPasswordAsync(Guid userId, string password);

    Task UpdateProfileAsync(Guid userId, string fullName);

    Task SetLastActiveTenantAsync(Guid userId, Guid? tenantId);

    Task SetLastMembershipEndedAsync(Guid userId, DateTimeOffset? endedAt);

    Task SetCleanupReminderSentAsync(Guid userId, DateTimeOffset sentAt);

    Task DeleteAsync(Guid userId);

    /// <summary>Temizlik işi için: hiçbir üyeliği olmayan veya e-postası doğrulanmamış kullanıcılar.</summary>
    Task<IReadOnlyList<UserSummary>> ListCleanupCandidatesAsync(CancellationToken ct);

    static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
