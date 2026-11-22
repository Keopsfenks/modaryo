namespace IdentityService.Application.Common.Interfaces;

/// <summary>
/// Anahtar bazlı (ör. e-posta) deneme sınırı. IP bazlı sınırlar ASP.NET rate limiter ile uygulanır;
/// e-posta istek gövdesinde olduğu için onun sınırı burada.
/// </summary>
public interface IAttemptLimiter
{
    /// <summary>Pencere içinde <paramref name="limit"/> aşılmadıysa true; aşıldıysa false.</summary>
    Task<bool> TryAcquireAsync(string key, int limit, TimeSpan window, CancellationToken ct);
}
