using IdentityService.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace IdentityService.Infrastructure.Security;

/// <summary>
/// Valkey sayacıyla sabit pencere sınırı: INCR + (ilk artışta) EXPIRE. "RateLimits:Enabled=false" iken her zaman izin verir.
/// </summary>
public sealed class AttemptLimiter(IConnectionMultiplexer redis, IConfiguration configuration) : IAttemptLimiter
{
    private readonly bool _enabled = configuration.GetValue("RateLimits:Enabled", true);

    public async Task<bool> TryAcquireAsync(string key, int limit, TimeSpan window, CancellationToken ct)
    {
        if (!_enabled)
        {
            return true;
        }

        var db = redis.GetDatabase();
        var redisKey = $"identity:limit:{key}";

        var count = await db.StringIncrementAsync(redisKey).WaitAsync(ct);
        await db.KeyExpireAsync(redisKey, window, ExpireWhen.HasNoExpiry).WaitAsync(ct);

        return count <= limit;
    }
}
