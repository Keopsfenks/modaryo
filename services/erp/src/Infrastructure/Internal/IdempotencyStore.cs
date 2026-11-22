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
                                                           TimeSpan          pendingTimeout,
                                                           CancellationToken ct
    ) {
        return await ReserveAsync(caller, key, tenantId, requestHash, pendingTimeout, retried: false, ct);
    }

    private async Task<IdempotencyReservation> ReserveAsync(string            caller,
                                                           string            key,
                                                           Guid              tenantId,
                                                           string            requestHash,
                                                           TimeSpan          pendingTimeout,
                                                           bool              retried,
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
        // Arada silinmiş (5xx sonrası serbest bırakılmış): en çok bir kez daha dene.
        if (existing is null)
            return retried
                       ? new IdempotencyReservation(IdempotencyOutcome.InProgress)
                       : await ReserveAsync(caller, key, tenantId, requestHash, pendingTimeout, true, ct);

        if (existing.RequestHash != requestHash) return new IdempotencyReservation(IdempotencyOutcome.Conflict);

        if (existing.Status != 0)
            return new IdempotencyReservation(IdempotencyOutcome.Replay, existing.Status, existing.ResponseJson);

        // Bekleyen kayıt zaman aşımına uğradıysa (işleyen süreç çökmüş olabilir) devral: sil ve bir kez yeniden ayır.
        if (!retried && existing.CreatedAt < time.GetUtcNow() - pendingTimeout) {
            var createdAt = existing.CreatedAt;
            await db.IdempotencyRecords
                .Where(r => r.Caller == caller && r.Key == key && r.Status == 0 && r.CreatedAt == createdAt)
                .ExecuteDeleteAsync(ct);
            return await ReserveAsync(caller, key, tenantId, requestHash, pendingTimeout, true, ct);
        }

        return new IdempotencyReservation(IdempotencyOutcome.InProgress);
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
