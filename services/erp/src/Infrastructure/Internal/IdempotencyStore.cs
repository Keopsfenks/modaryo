using ERP.Domain.Platform;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Modaryo.Internal;
using Npgsql;

namespace ERP.Infrastructure.Internal;

/// <summary>İç API idempotency kayıtları (PostgreSQL). Önce ayırır, sonra tamamlar; 5xx/istisnada siler.</summary>
public sealed class IdempotencyStore(
    ApplicationDbContext db,
    TimeProvider         time
) : IInternalIdempotencyStore {
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public async Task<IdempotencyReservation> ReserveAsync(string            caller,
                                                           string            key,
                                                           Guid              tenantId,
                                                           string            requestHash,
                                                           CancellationToken ct
    ) {
        var record = new IdempotencyRecord {
            Caller = caller, Key = key, TenantId = tenantId, RequestHash = requestHash, Status = 0,
            ResponseJson = string.Empty, CreatedAt = time.GetUtcNow()
        };
        db.IdempotencyRecords.Add(record);
        try {
            await db.SaveChangesAsync(ct);
            return new IdempotencyReservation(IdempotencyOutcome.Reserved);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) {
            db.Entry(record).State = EntityState.Detached;
        }

        var existing = await db.IdempotencyRecords.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.Caller == caller && r.Key == key, ct);
        if (existing is null) // arada silinmiş (5xx sonrası serbest bırakılmış): bir kez daha dene
            return await ReserveAsync(caller, key, tenantId, requestHash, ct);

        if (existing.RequestHash != requestHash) return new IdempotencyReservation(IdempotencyOutcome.Conflict);

        return existing.Status == 0
                   ? new IdempotencyReservation(IdempotencyOutcome.InProgress)
                   : new IdempotencyReservation(IdempotencyOutcome.Replay, existing.Status, existing.ResponseJson);
    }

    public async Task CompleteAsync(string caller, string key, int status, string responseJson, CancellationToken ct) {
        await db.IdempotencyRecords.Where(r => r.Caller == caller && r.Key == key)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status,       status)
                                    .SetProperty(r => r.ResponseJson, responseJson), ct);
    }

    public async Task ReleaseAsync(string caller, string key, CancellationToken ct) {
        await db.IdempotencyRecords.Where(r => r.Caller == caller && r.Key == key).ExecuteDeleteAsync(ct);
    }

    /// <summary>7 günden eski kayıtları siler; silinen kayıt sayısını döndürür.</summary>
    public async Task<int> PurgeAsync(CancellationToken ct) {
        var cutoff = time.GetUtcNow() - Retention;
        return await db.IdempotencyRecords.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
