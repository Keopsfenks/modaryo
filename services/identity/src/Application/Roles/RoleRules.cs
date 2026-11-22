using System.Globalization;
using FluentValidation.Results;
using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Roles;

/// <summary>Rol komutlarının ortak kuralları: doğrulama, yetki yükseltme koruması, ad benzersizliği, şube kontrolü, DTO.</summary>
public sealed class RoleRules(IApplicationDbContext db, CompanyAccess access, ICurrentSession current)
{
    private static readonly CultureInfo Turkish = new("tr-TR");
    private static readonly string[] SystemOrder = [SystemRoles.Owner, SystemRoles.Admin, SystemRoles.Member];

    public const int NameMin = 2;
    public const int NameMax = 50;

    public static bool IsValidDiscount(decimal value) => value is >= 0 and <= 100 && decimal.Round(value, 2) == value;

    /// <summary>Sahip rolü hiçbir şekilde değiştirilemez.</summary>
    public static void EnsureNotOwner(TenantRole role)
    {
        if (role.SystemKey == SystemRoles.Owner)
        {
            throw new AuthException(StatusCodes.Status409Conflict, AuthErrorCodes.RoleLocked, "Sahip rolü değiştirilemez.");
        }
    }

    public static IdentityService.Application.Common.Exceptions.ValidationException FieldError(string field, string message) =>
        new([new ValidationFailure(field, message)]);

    /// <summary>Bilinmeyen izin ve "*" kabul edilmez; yinelenenler tekilleştirilir.</summary>
    public static string[] NormalizePermissions(IEnumerable<string> requested)
    {
        var list = requested.Select(p => p?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToArray();
        var bad = list.FirstOrDefault(p => p == Permissions.All || !PermissionCatalog.IsKnown(p));
        if (bad is not null)
        {
            throw FieldError("permissions", $"Geçersiz izin: {bad}.");
        }

        return list;
    }

    /// <summary>
    /// Çağıran "*" taşımıyorsa, yeni kümede olup eski kümede bulunmayan her izni kendisi de taşımalıdır.
    /// Oluşturmada eski küme boştur.
    /// </summary>
    public void EnsureGrantable(IEnumerable<string> newSet, IEnumerable<string> oldSet)
    {
        if (current.HasPermission(Permissions.All))
        {
            return;
        }

        var held = current.Permissions.ToHashSet(StringComparer.Ordinal);
        var old = oldSet.ToHashSet(StringComparer.Ordinal);
        var denied = newSet.FirstOrDefault(p => !old.Contains(p) && !held.Contains(p));
        if (denied is not null)
        {
            throw new AuthException(StatusCodes.Status403Forbidden, AuthErrorCodes.PermissionNotGrantable,
                $"Sahip olmadığınız bir izni rolde veremezsiniz: {denied}.");
        }
    }

    public async Task EnsureNameFreeAsync(string name, Guid? exceptRoleId, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var names = await db.TenantRoles.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Id != exceptRoleId)
            .Select(r => r.Name)
            .ToListAsync(ct);

        if (names.Any(n => string.Compare(n, name, Turkish, CompareOptions.IgnoreCase) == 0))
        {
            throw new AuthException(StatusCodes.Status409Conflict, AuthErrorCodes.RoleNameTaken,
                "Bu adda bir rol zaten var.");
        }
    }

    /// <summary>allBranches=false ise şubeler boş olmamalı ve firmanın aktif şubeleri olmalı.</summary>
    public async Task<Guid[]> ResolveBranchesAsync(bool allBranches, IEnumerable<Guid> branchIds, CancellationToken ct)
    {
        if (allBranches)
        {
            return [];
        }

        var ids = branchIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            throw FieldError("branchIds", "Tüm şubelere erişim kapalıysa en az bir şube seçilmelidir.");
        }

        var tenantId = access.TenantId;
        var valid = await db.Branches.AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.IsActive && ids.Contains(b.Id))
            .CountAsync(ct);
        if (valid != ids.Length)
        {
            throw FieldError("branchIds", "Seçilen şubelerden biri bu firmaya ait değil veya pasif.");
        }

        return ids;
    }

    /// <summary>Kümeyi hedefe uyarlar: yalnızca eksikleri ekler, fazlaları çıkarır.</summary>
    public static void ApplyPermissions(TenantRole role, string[] permissions)
    {
        role.Permissions.RemoveAll(p => !permissions.Contains(p.Permission, StringComparer.Ordinal));
        var existing = role.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);
        role.Permissions.AddRange(permissions.Where(p => !existing.Contains(p))
            .Select(p => new TenantRolePermission { RoleId = role.Id, Permission = p }));
    }

    public static void ApplyBranches(TenantRole role, Guid[] branchIds)
    {
        role.Branches.RemoveAll(b => !branchIds.Contains(b.BranchId));
        var existing = role.Branches.Select(b => b.BranchId).ToHashSet();
        role.Branches.AddRange(branchIds.Where(b => !existing.Contains(b))
            .Select(b => new RoleBranch { RoleId = role.Id, BranchId = b }));
    }

    /// <summary>Kopyalamada sahip rolün "*" izni katalogdaki tüm izinlere açılır.</summary>
    public static string[] ExpandedPermissions(TenantRole role) =>
        role.Permissions.Any(p => p.Permission == Permissions.All)
            ? PermissionCatalog.All.Select(p => p.Code).ToArray()
            : role.Permissions.Select(p => p.Permission).ToArray();

    public async Task<TenantRole> LoadAsync(Guid roleId, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        return await db.TenantRoles.Include(r => r.Branches)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == roleId, ct)
            ?? throw new NotFoundException(roleId.ToString(), "Role");
    }

    public async Task<RoleDto> ToDtoAsync(TenantRole role, CancellationToken ct)
    {
        var count = await db.Memberships.CountAsync(m => m.RoleId == role.Id, ct);
        return ToDto(role, count);
    }

    public static RoleDto ToDto(TenantRole role, int memberCount) => new(
        role.Id,
        role.Name,
        role.SystemKey,
        role.SystemKey == SystemRoles.Owner,
        role.SystemKey == SystemRoles.Owner
            ? [Permissions.All]
            : role.Permissions.Select(p => p.Permission).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
        role.MaxDiscountPercent,
        role.AllBranches,
        role.Branches.Select(b => b.BranchId).Order().ToArray(),
        memberCount);

    public static int SystemRank(TenantRole role) =>
        role.SystemKey is null ? int.MaxValue : Array.IndexOf(SystemOrder, role.SystemKey);

    /// <summary>Listeleme/okuma: roller.read veya (üye ekranları için) members.read yeterlidir.</summary>
    public void EnsureCanRead()
    {
        if (current.TenantId is null
            || !(current.HasPermission(Permissions.RolesRead) || current.HasPermission(Permissions.MembersRead)))
        {
            throw new ForbiddenAccessException();
        }
    }
}
