namespace IdentityService.Application.Common.Interfaces;

public sealed record NewSession(
    Guid UserId,
    string Email,
    string FullName,
    Auth.SessionGrant Grant,
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

    /// <summary>Tek oturumun yetki paketini (firma, izinler, modüller, şubeler...) günceller. Oturum yoksa false.</summary>
    Task<bool> UpdateGrantAsync(string sidHash, Auth.SessionGrant grant, CancellationToken ct);

    /// <summary>
    /// Kullanıcının aktif firması <paramref name="tenantId"/> olan oturumlarının yetki paketini değiştirir
    /// (<paramref name="grant"/> başka firmaya ya da <see cref="Auth.SessionGrant.None"/>'a işaret edebilir).
    /// </summary>
    Task UpdateGrantForUserAsync(Guid userId, Guid tenantId, Auth.SessionGrant grant, CancellationToken ct);

    /// <summary>Kullanıcının canlı oturumları; süresi dolmuş index üyelerini temizler.</summary>
    Task<IReadOnlyList<StoredSession>> ListForUserAsync(Guid userId, CancellationToken ct);

    Task DeleteAsync(string sidHash, CancellationToken ct);

    Task<bool> DeleteByPublicIdAsync(Guid userId, string publicId, CancellationToken ct);

    Task DeleteAllForUserAsync(Guid userId, string? exceptSidHash, CancellationToken ct);
}
