using System.Text.Json;
using IdentityService.Application.Auth;
using IdentityService.Application.Common.Interfaces;
using Modaryo.Sessions;
using StackExchange.Redis;

namespace IdentityService.Infrastructure.Sessions;

/// <summary>
/// Valkey'deki oturum kayıtlarının tek yazıcısı (spec 6.5).
/// Oluşturma: SET NX EX. Güncelleme: SET KEEPTTL XX (silinmiş oturum dirilmez, süre uzamaz).
/// </summary>
public sealed class SessionStore(IConnectionMultiplexer redis, TimeProvider time) : ISessionStore
{
    private IDatabase Db => redis.GetDatabase();

    public async Task<CreatedSession> CreateAsync(NewSession session, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var idle = session.RememberMe ? SessionTtl.RememberIdle : SessionTtl.RegularIdle;
        var absolute = session.RememberMe ? SessionTtl.RememberAbsolute : SessionTtl.RegularAbsolute;

        var record = new SessionRecord
        {
            V = SessionKeys.CurrentVersion,
            Id = SessionKeys.NewPublicId(),
            UserId = session.UserId,
            Email = session.Email,
            FullName = session.FullName,
            TenantId = session.Grant.TenantId,
            Permissions = session.Grant.Permissions,
            Modules = session.Grant.Modules,
            BranchIds = session.Grant.BranchIds,
            MaxDiscountPct = session.Grant.MaxDiscountPct,
            UserType = session.Grant.UserType,
            CariId = session.Grant.CariId,
            RememberMe = session.RememberMe,
            CreatedAt = now.ToUnixTimeSeconds(),
            AbsoluteExpiresAt = now.Add(absolute).ToUnixTimeSeconds(),
            IdleTimeoutS = (int)idle.TotalSeconds,
            Ip = session.Ip,
            UserAgent = session.UserAgent,
        };

        var ttl = SessionTtl.Compute(record, now)!.Value;
        var json = Serialize(record);

        while (true)
        {
            var sid = SessionKeys.NewSid();
            var sidHash = SessionKeys.HashSid(sid);

            if (await Db.StringSetAsync(SessionKeys.SessionKey(sidHash), json, ttl, When.NotExists).WaitAsync(ct))
            {
                var indexKey = SessionKeys.UserIndexKey(session.UserId);
                await Db.SetAddAsync(indexKey, sidHash).WaitAsync(ct);
                await Db.KeyExpireAsync(indexKey, SessionTtl.RememberAbsolute).WaitAsync(ct);
                return new CreatedSession(sid, sidHash, record);
            }
        }
    }

    public async Task<bool> UpdateGrantAsync(string sidHash, SessionGrant grant, CancellationToken ct)
    {
        var record = await GetAsync(sidHash, ct);
        if (record is null)
        {
            return false;
        }

        return await ReplaceAsync(sidHash, Apply(record, grant), ct);
    }

    public async Task UpdateGrantForUserAsync(Guid userId, Guid tenantId, SessionGrant grant, CancellationToken ct)
    {
        foreach (var session in await ListForUserAsync(userId, ct))
        {
            if (session.Record.TenantId == tenantId)
            {
                await ReplaceAsync(session.SidHash, Apply(session.Record, grant), ct);
            }
        }
    }

    private static SessionRecord Apply(SessionRecord record, SessionGrant grant) => record with
    {
        TenantId = grant.TenantId,
        Permissions = grant.Permissions,
        Modules = grant.Modules,
        BranchIds = grant.BranchIds,
        MaxDiscountPct = grant.MaxDiscountPct,
        UserType = grant.UserType,
        CariId = grant.CariId,
    };

    public async Task<IReadOnlyList<StoredSession>> ListForUserAsync(Guid userId, CancellationToken ct)
    {
        var indexKey = SessionKeys.UserIndexKey(userId);
        var sessions = new List<StoredSession>();

        foreach (var member in await Db.SetMembersAsync(indexKey).WaitAsync(ct))
        {
            var sidHash = member.ToString();
            var record = await GetAsync(sidHash, ct);

            if (record is null || record.UserId != userId)
            {
                await Db.SetRemoveAsync(indexKey, member).WaitAsync(ct);
                continue;
            }

            sessions.Add(new StoredSession(sidHash, record));
        }

        return sessions.OrderByDescending(s => s.Record.CreatedAt).ToList();
    }

    public async Task DeleteAsync(string sidHash, CancellationToken ct)
    {
        await Db.KeyDeleteAsync(SessionKeys.SessionKey(sidHash)).WaitAsync(ct);
    }

    public async Task<bool> DeleteByPublicIdAsync(Guid userId, string publicId, CancellationToken ct)
    {
        var session = (await ListForUserAsync(userId, ct)).FirstOrDefault(s => s.Record.Id == publicId);
        if (session is null)
        {
            return false;
        }

        await DeleteAsync(session.SidHash, ct);
        await Db.SetRemoveAsync(SessionKeys.UserIndexKey(userId), session.SidHash).WaitAsync(ct);
        return true;
    }

    public async Task DeleteAllForUserAsync(Guid userId, string? exceptSidHash, CancellationToken ct)
    {
        var indexKey = SessionKeys.UserIndexKey(userId);

        foreach (var session in await ListForUserAsync(userId, ct))
        {
            if (session.SidHash == exceptSidHash)
            {
                continue;
            }

            await DeleteAsync(session.SidHash, ct);
            await Db.SetRemoveAsync(indexKey, session.SidHash).WaitAsync(ct);
        }
    }

    private async Task<SessionRecord?> GetAsync(string sidHash, CancellationToken ct)
    {
        var raw = await Db.StringGetAsync(SessionKeys.SessionKey(sidHash)).WaitAsync(ct);
        if (raw.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            var record = JsonSerializer.Deserialize<SessionRecord>(raw.ToString(), SessionRecord.Json);
            return record?.V == SessionKeys.CurrentVersion ? record : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Yalnızca anahtar hâlâ varsa yazar (XX) ve mevcut TTL'i korur (KEEPTTL).</summary>
    private async Task<bool> ReplaceAsync(string sidHash, SessionRecord record, CancellationToken ct) =>
        await Db.StringSetAsync(SessionKeys.SessionKey(sidHash), Serialize(record), null, keepTtl: true, When.Exists).WaitAsync(ct);

    private static string Serialize(SessionRecord record) => JsonSerializer.Serialize(record, SessionRecord.Json);
}
