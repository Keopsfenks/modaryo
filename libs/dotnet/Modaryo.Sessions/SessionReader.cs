using System.Text.Json;
using StackExchange.Redis;

namespace Modaryo.Sessions;

public sealed record SessionLookup(string SidHash, SessionRecord Record);

/// <summary>
/// Oturum okuyucu (spec 6.6). Kaydı yalnızca okur ve süresini uzatır; asla yazmaz veya silmez.
/// </summary>
public sealed class SessionReader(IConnectionMultiplexer redis, TimeProvider time)
{
    public async Task<SessionLookup?> ReadAsync(string sid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            return null;
        }

        var sidHash = SessionKeys.HashSid(sid);
        var key = SessionKeys.SessionKey(sidHash);
        var db = redis.GetDatabase();

        var raw = await db.StringGetAsync(key).WaitAsync(ct);
        if (raw.IsNullOrEmpty)
        {
            return null;
        }

        SessionRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<SessionRecord>(raw.ToString(), SessionRecord.Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (record is null || record.V != SessionKeys.CurrentVersion)
        {
            return null;
        }

        var ttl = SessionTtl.Compute(record, time.GetUtcNow());
        if (ttl is null)
        {
            return null;
        }

        await db.KeyExpireAsync(key, ttl).WaitAsync(ct);
        return new SessionLookup(sidHash, record);
    }
}
