namespace IdentityService.Application.Common.Interfaces;

public sealed record NewSession(
    Guid UserId,
    string Email,
    string FullName,
    Guid? TenantId,
    string[] Permissions,
    bool RememberMe,
    string? Ip,
    string? UserAgent);

/// <summary><see cref="Sid"/> yalnızca cookie'ye yazılır; hiçbir yerde saklanmaz.</summary>
public sealed record CreatedSession(string Sid, string SidHash, SessionRecord Record);

public sealed record StoredSession(string SidHash, SessionRecord Record);

/// <summary>
/// Oturum kayıtlarının tek yazıcısı (spec 6.5). Güncellemeler silinmiş bir oturumu asla geri getirmez
/// ve süresini asla uzatmaz.
/// </summary>
public interface ISessionStore
{
    Task<CreatedSession> CreateAsync(NewSession session, CancellationToken ct);

    /// <summary>Tek oturumun aktif firmasını ve izinlerini günceller. Oturum yoksa false.</summary>
    Task<bool> UpdateTenantAsync(string sidHash, Guid? tenantId, string[] permissions, CancellationToken ct);

    /// <summary>Kullanıcının yalnızca <paramref name="tenantId"/> firmasında açık olan oturumlarını günceller.</summary>
    Task UpdateTenantForUserAsync(Guid userId, Guid tenantId, Guid? newTenantId, string[] permissions, CancellationToken ct);

    /// <summary>Kullanıcının canlı oturumları; süresi dolmuş index üyelerini temizler.</summary>
    Task<IReadOnlyList<StoredSession>> ListForUserAsync(Guid userId, CancellationToken ct);

    Task DeleteAsync(string sidHash, CancellationToken ct);

    Task<bool> DeleteByPublicIdAsync(Guid userId, string publicId, CancellationToken ct);

    Task DeleteAllForUserAsync(Guid userId, string? exceptSidHash, CancellationToken ct);
}
