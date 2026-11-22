using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Data;

/// <summary>
/// Hazır rollerin (owner/admin/member) varsayılan izinlerini mevcut firmalara dağıtır.
/// Yalnızca ekler, asla izin silmez; SystemKey'i olmayan özel rollere dokunmaz. Idempotenttir.
/// </summary>
public sealed class SystemRolePermissionSync(ApplicationDbContext db)
{
    /// <summary>Eklenen izin satırı sayısını döner.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var roles = await db.TenantRoles
            .Include(r => r.Permissions)
            .Where(r => r.SystemKey != null)
            .ToListAsync(ct);

        var added = 0;
        foreach (var role in roles)
        {
            if (!SystemRoles.DefaultPermissions.TryGetValue(role.SystemKey!, out var defaults))
            {
                continue;
            }

            var existing = role.Permissions.Select(p => p.Permission).ToHashSet();
            foreach (var permission in defaults.Where(existing.Add))
            {
                role.Permissions.Add(new TenantRolePermission { RoleId = role.Id, Permission = permission });
                added++;
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return added;
    }
}
