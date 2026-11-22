using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ValidationFailure = FluentValidation.Results.ValidationFailure;

namespace IdentityService.Infrastructure.Identity;

/// <summary>UserManager sarmalayıcısı; e-postalar her zaman normalize edilir (kırpılmış, küçük harf).</summary>
public sealed class UserAccounts(UserManager<ApplicationUser> users, ApplicationDbContext db, TimeProvider time) : IUserAccounts
{
    public async Task<UserSummary?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(IUserAccounts.Normalize(email));
        return user is null ? null : ToSummary(user);
    }

    public async Task<UserSummary?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        return user is null ? null : ToSummary(user);
    }

    public async Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var idList = ids.Distinct().ToList();
        var found = await db.Users.AsNoTracking().Where(u => idList.Contains(u.Id)).ToListAsync(ct);
        return found.ToDictionary(u => u.Id, ToSummary);
    }

    public async Task<UserSummary> CreateAsync(string email, string fullName, string password, bool emailConfirmed, CancellationToken ct)
    {
        var normalized = IUserAccounts.Normalize(email);
        var now = time.GetUtcNow();
        var user = new ApplicationUser
        {
            UserName = normalized,
            Email = normalized,
            FullName = fullName.Trim(),
            CreatedAt = now,
            EmailConfirmed = emailConfirmed,
            EmailConfirmedAt = emailConfirmed ? now : null,
        };

        ThrowIfFailed(await users.CreateAsync(user, password), "Password");
        return ToSummary(user);
    }

    public async Task<string> CreateEmailConfirmationTokenAsync(Guid userId) =>
        await users.GenerateEmailConfirmationTokenAsync(await GetAsync(userId));

    public async Task<bool> ConfirmEmailAsync(Guid userId, string token)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var wasConfirmed = user.EmailConfirmed;
        if (!(await users.ConfirmEmailAsync(user, token)).Succeeded)
        {
            return false;
        }

        if (!wasConfirmed)
        {
            user.EmailConfirmedAt = time.GetUtcNow();
            await users.UpdateAsync(user);
        }

        return true;
    }

    public async Task MarkEmailConfirmedAsync(Guid userId)
    {
        var user = await GetAsync(userId);
        if (user.EmailConfirmed)
        {
            return;
        }

        user.EmailConfirmed = true;
        user.EmailConfirmedAt = time.GetUtcNow();
        await users.UpdateAsync(user);
    }

    public async Task<PasswordCheck> CheckPasswordAsync(string email, string password)
    {
        var user = await users.FindByEmailAsync(IUserAccounts.Normalize(email));
        if (user is null)
        {
            return new PasswordCheck(PasswordCheckResult.InvalidCredentials, null, null);
        }

        if (await users.IsLockedOutAsync(user))
        {
            return new PasswordCheck(PasswordCheckResult.LockedOut, ToSummary(user), user.LockoutEnd);
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return await users.IsLockedOutAsync(user)
                ? new PasswordCheck(PasswordCheckResult.LockedOut, ToSummary(user), user.LockoutEnd)
                : new PasswordCheck(PasswordCheckResult.InvalidCredentials, null, null);
        }

        await users.ResetAccessFailedCountAsync(user);

        return user.EmailConfirmed
            ? new PasswordCheck(PasswordCheckResult.Success, ToSummary(user), null)
            : new PasswordCheck(PasswordCheckResult.NotConfirmed, ToSummary(user), null);
    }

    public async Task<string> CreatePasswordResetTokenAsync(Guid userId) =>
        await users.GeneratePasswordResetTokenAsync(await GetAsync(userId));

    public async Task<bool> ResetPasswordAsync(string email, string token, string newPassword)
    {
        var user = await users.FindByEmailAsync(IUserAccounts.Normalize(email));
        if (user is null)
        {
            return false;
        }

        var result = await users.ResetPasswordAsync(user, token, newPassword);
        if (result.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal)))
        {
            ThrowIfFailed(result, "NewPassword");
        }

        if (result.Succeeded)
        {
            // Sıfırlama e-postası kişinin e-postayı okuduğunu da kanıtlar; kilidi de kaldır.
            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);
        }

        return result.Succeeded;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await GetAsync(userId);
        if (!await users.CheckPasswordAsync(user, currentPassword))
        {
            return false;
        }

        ThrowIfFailed(await users.ChangePasswordAsync(user, currentPassword, newPassword), "NewPassword");
        return true;
    }

    public async Task SetPasswordAsync(Guid userId, string password)
    {
        var user = await GetAsync(userId);
        if (await users.HasPasswordAsync(user))
        {
            ThrowIfFailed(await users.RemovePasswordAsync(user), "Password");
        }

        ThrowIfFailed(await users.AddPasswordAsync(user, password), "Password");
    }

    public async Task UpdateProfileAsync(Guid userId, string fullName)
    {
        var user = await GetAsync(userId);
        user.FullName = fullName.Trim();
        await users.UpdateAsync(user);
    }

    public async Task SetLastActiveTenantAsync(Guid userId, Guid? tenantId)
    {
        var user = await GetAsync(userId);
        user.LastActiveTenantId = tenantId;
        await users.UpdateAsync(user);
    }

    public async Task SetLastMembershipEndedAsync(Guid userId, DateTimeOffset? endedAt)
    {
        var user = await GetAsync(userId);
        user.LastMembershipEndedAt = endedAt;
        user.CleanupReminderSentAt = null;
        await users.UpdateAsync(user);
    }

    public async Task SetCleanupReminderSentAsync(Guid userId, DateTimeOffset sentAt)
    {
        var user = await GetAsync(userId);
        user.CleanupReminderSentAt = sentAt;
        await users.UpdateAsync(user);
    }

    public async Task DeleteAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is not null)
        {
            await users.DeleteAsync(user);
        }
    }

    public async Task<IReadOnlyList<UserSummary>> ListCleanupCandidatesAsync(CancellationToken ct)
    {
        var candidates = await db.Users.AsNoTracking()
            .Where(u => !u.EmailConfirmed || !db.Memberships.Any(m => m.UserId == u.Id))
            .ToListAsync(ct);
        return candidates.Select(ToSummary).ToList();
    }

    private async Task<ApplicationUser> GetAsync(Guid userId) =>
        await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException(userId.ToString(), "User");

    private static void ThrowIfFailed(IdentityResult result, string propertyName)
    {
        if (!result.Succeeded)
        {
            throw new ValidationException(result.Errors.Select(e => new ValidationFailure(propertyName, Translate(e))));
        }
    }

    private static string Translate(IdentityError error) => error.Code switch
    {
        nameof(IdentityErrorDescriber.PasswordTooShort) => "Şifre en az 10 karakter olmalıdır.",
        nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName) => "Bu e-posta adresi kullanılamaz.",
        nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) => "Geçerli bir e-posta adresi girin.",
        nameof(IdentityErrorDescriber.PasswordMismatch) => "Mevcut şifre hatalı.",
        _ => error.Description,
    };

    private static UserSummary ToSummary(ApplicationUser user) => new(
        user.Id,
        user.Email ?? string.Empty,
        user.FullName,
        user.EmailConfirmed,
        user.LastActiveTenantId,
        user.CreatedAt,
        user.EmailConfirmedAt,
        user.LastMembershipEndedAt,
        user.CleanupReminderSentAt);
}
