using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdentityService.Infrastructure.Data;

/// <summary>
/// Hazır rollere (admin/member) yeni izin seti sürümlerini her firmaya bir kez uygular.
/// Uygulanan sürüm firmada tutulur; işlem bitince dokunulan firmaların açık oturumları yenilenir; yöneticinin kaldırdığı izin bir sonraki açılışta geri eklenmez.
/// SystemKey'i olmayan özel rollere dokunmaz. İki örnek aynı anda açılırsa PostgreSQL advisory kilidi ile sıraya girer.
/// </summary>
public sealed class SystemRolePermissionSync(ApplicationDbContext db, MembershipChanges changes, ILogger<SystemRolePermissionSync> logger)
{
    private const long AdvisoryLockKey = 727001;

    /// <summary>Eklenen izin satırı sayısını döner.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var latest = SystemRoles.LatestPermissionSetVersion;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", ct);

        var tenants = await db.Tenants
            .Where(t => t.AppliedPermissionSetVersion < latest)
            .ToListAsync(ct);
        if (tenants.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return 0;
        }

        var tenantIds = tenants.Select(t => t.Id).ToList();
        var roles = await db.TenantRoles
            .Include(r => r.Permissions)
            .Where(r => r.SystemKey != null && tenantIds.Contains(r.TenantId))
            .ToListAsync(ct);

        var added = 0;
        foreach (var tenant in tenants)
        {
            var pending = SystemRoles.PermissionSetVersions.Where(v => v.Version > tenant.AppliedPermissionSetVersion).ToList();
            foreach (var role in roles.Where(r => r.TenantId == tenant.Id))
            {
                var codes = role.SystemKey switch
                {
                    SystemRoles.Admin => pending.SelectMany(v => v.AddToAdmin),
                    SystemRoles.Member => pending.SelectMany(v => v.AddToMember),
                    _ => [],
                };

                var existing = role.Permissions.Select(p => p.Permission).ToHashSet();
                foreach (var permission in codes.Where(existing.Add))
                {
                    role.Permissions.Add(new TenantRolePermission { RoleId = role.Id, Permission = permission });
                    added++;
                }
            }

            tenant.AppliedPermissionSetVersion = latest;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // Kilit ve işlem dışında: dokunulan firmaların açık oturumları yeni izin setiyle yenilenir (en iyi çaba).
        // Valkey kapalıysa açılış başarısız olmasın: DB'deki işlem geçerlidir, oturumlar sonraki girişte ya da rol değişikliğinde yenilenir.
        foreach (var tenant in tenants)
        {
            try
            {
                await changes.RefreshTenantAsync(tenant.Id, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Firma {TenantId} için açık oturumlar yenilenemedi; sonraki girişte ya da rol değişikliğinde yenilenecek.", tenant.Id);
            }
        }

        return added;
    }
}
