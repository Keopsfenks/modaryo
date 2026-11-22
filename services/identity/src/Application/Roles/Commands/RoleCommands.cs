using IdentityService.Application.Audit;
using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Roles.Commands;

/// <summary>
/// Rol gövdesi. Alanlar boş bırakılırsa: oluşturmada kopyalanan rolün (yoksa varsayılan) değerleri,
/// güncellemede mevcut değerler korunur.
/// </summary>
public abstract record RoleWriteBase
{
    public string Name { get; init; } = string.Empty;

    public string[]? Permissions { get; init; }

    public decimal? MaxDiscountPercent { get; init; }

    public bool? AllBranches { get; init; }

    public Guid[]? BranchIds { get; init; }
}

public abstract class RoleWriteValidator<T> : AbstractValidator<T> where T : RoleWriteBase
{
    protected RoleWriteValidator()
    {
        RuleFor(c => c.Name)
            .Must(n => n is not null && n.Trim().Length >= RoleRules.NameMin).WithMessage("Rol adı en az 2 karakter olmalıdır.")
            .Must(n => n is null || n.Trim().Length <= RoleRules.NameMax).WithMessage("Rol adı en fazla 50 karakter olabilir.");
        RuleFor(c => c.MaxDiscountPercent)
            .Must(d => d is null || RoleRules.IsValidDiscount(d.Value))
            .WithMessage("İskonto sınırı 0 ile 100 arasında olmalı ve en fazla 2 ondalık basamak içermelidir.");
    }
}

[Authorize(Permission = Domain.Constants.Permissions.RolesManage)]
public record CreateRoleCommand : RoleWriteBase, IRequest<RoleDto>
{
    public Guid? CopyFromRoleId { get; init; }
}

public class CreateRoleCommandValidator : RoleWriteValidator<CreateRoleCommand>;

public class CreateRoleCommandHandler(IApplicationDbContext db, CompanyAccess access, RoleRules rules, MembershipChanges changes, IAuditLog audit)
    : IRequestHandler<CreateRoleCommand, RoleDto>
{
    public async Task<RoleDto> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        var source = request.CopyFromRoleId is { } copyId ? await rules.LoadAsync(copyId, ct) : null;

        var name = request.Name.Trim();
        var permissions = RoleRules.NormalizePermissions(request.Permissions ?? (source is null ? [] : RoleRules.ExpandedPermissions(source)));
        var allBranches = request.AllBranches ?? source?.AllBranches ?? true;
        var branchSource = request.BranchIds ?? (source?.Branches.Select(b => b.BranchId).ToArray() ?? []);
        var discount = request.MaxDiscountPercent ?? source?.MaxDiscountPercent ?? 0m;

        rules.EnsureGrantable(permissions, []);
        await rules.EnsureNameFreeAsync(name, null, ct);
        var branchIds = await rules.ResolveBranchesAsync(allBranches, branchSource, ct);
        rules.EnsureScopeGrantable(discount, allBranches, branchIds, null);

        var role = new TenantRole { TenantId = access.TenantId, Name = name, MaxDiscountPercent = discount, AllBranches = allBranches };
        RoleRules.ApplyPermissions(role, permissions);
        RoleRules.ApplyBranches(role, branchIds);
        db.TenantRoles.Add(role);
        audit.Record(AuditActions.RoleCreated, AuditEntities.Role, role.Id.ToString(), null, AuditSnapshots.Role(role));
        await db.SaveChangesAsync(ct);

        await changes.OnRoleDefinitionChangedAsync(role.Id, ct);
        return RoleRules.ToDto(role, 0);
    }
}

[Authorize(Permission = Domain.Constants.Permissions.RolesManage)]
public record UpdateRoleCommand : RoleWriteBase, IRequest<RoleDto>
{
    public Guid Id { get; init; }
}

public class UpdateRoleCommandValidator : RoleWriteValidator<UpdateRoleCommand>;

public class UpdateRoleCommandHandler(IApplicationDbContext db, RoleRules rules, MembershipChanges changes, IAuditLog audit)
    : IRequestHandler<UpdateRoleCommand, RoleDto>
{
    public async Task<RoleDto> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        var role = await rules.LoadAsync(request.Id, ct);
        RoleRules.EnsureNotOwner(role);

        var name = request.Name.Trim();
        var oldPermissions = role.Permissions.Select(p => p.Permission).ToArray();
        var permissions = RoleRules.NormalizePermissions(request.Permissions ?? oldPermissions);
        var allBranches = request.AllBranches ?? role.AllBranches;
        var existingBranchIds = role.Branches.Select(b => b.BranchId).ToArray();
        var branchSource = request.BranchIds ?? existingBranchIds;

        rules.EnsureGrantable(permissions, oldPermissions);
        await rules.EnsureNameFreeAsync(name, role.Id, ct);
        var branchIds = await rules.ResolveBranchesAsync(allBranches, branchSource, ct, existingBranchIds);
        var discount = request.MaxDiscountPercent ?? role.MaxDiscountPercent;
        rules.EnsureScopeGrantable(discount, allBranches, branchIds,
            (role.MaxDiscountPercent, role.AllBranches, role.Branches.Select(b => b.BranchId).ToArray()));

        var before = AuditSnapshots.Role(role);
        role.Name = name;
        role.MaxDiscountPercent = discount;
        role.AllBranches = allBranches;
        RoleRules.ApplyPermissions(role, permissions);
        RoleRules.ApplyBranches(role, branchIds);
        audit.Record(AuditActions.RoleUpdated, AuditEntities.Role, role.Id.ToString(), before, AuditSnapshots.Role(role));
        await db.SaveChangesAsync(ct);

        await changes.OnRoleDefinitionChangedAsync(role.Id, ct);
        return await rules.ToDtoAsync(role, ct);
    }
}

[Authorize(Permission = Domain.Constants.Permissions.RolesManage)]
public record DeleteRoleCommand(Guid Id) : IRequest;

public class DeleteRoleCommandHandler(IApplicationDbContext db, RoleRules rules, MembershipChanges changes, TimeProvider time, IAuditLog audit)
    : IRequestHandler<DeleteRoleCommand>
{
    public async Task Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var role = await rules.LoadAsync(request.Id, ct);
        if (role.IsSystem || role.SystemKey is not null)
        {
            throw new AuthException(StatusCodes.Status409Conflict, AuthErrorCodes.RoleLocked, "Hazır roller silinemez.");
        }

        var now = time.GetUtcNow();
        var invitations = await db.Invitations.Where(i => i.RoleId == role.Id).ToListAsync(ct);
        if (await db.Memberships.AnyAsync(m => m.RoleId == role.Id, ct) || invitations.Any(i => i.IsPending(now)))
        {
            throw new AuthException(StatusCodes.Status409Conflict, AuthErrorCodes.RoleInUse,
                "Bu rolü kullanan üyeler veya bekleyen davetler var. Önce onları başka bir role taşıyın.");
        }

        // Kapanmış (kabul/iptal/süresi dolmuş) davetler rolü tutmaya devam edemez.
        db.Invitations.RemoveRange(invitations);
        db.TenantRoles.Remove(role);
        audit.Record(AuditActions.RoleDeleted, AuditEntities.Role, role.Id.ToString(), AuditSnapshots.Role(role), null);
        await db.SaveChangesAsync(ct);

        await changes.OnRoleDefinitionChangedAsync(role.Id, ct);
    }
}

/// <summary>Yönetici/Kullanıcı rolünün izinlerini hazır varsayılana döndürür.</summary>
[Authorize(Permission = Domain.Constants.Permissions.RolesManage)]
public record ResetRoleCommand(Guid Id) : IRequest<RoleDto>;

public class ResetRoleCommandHandler(IApplicationDbContext db, RoleRules rules, MembershipChanges changes, IAuditLog audit)
    : IRequestHandler<ResetRoleCommand, RoleDto>
{
    public async Task<RoleDto> Handle(ResetRoleCommand request, CancellationToken ct)
    {
        var role = await rules.LoadAsync(request.Id, ct);
        if (role.SystemKey is not (SystemRoles.Admin or SystemRoles.Member))
        {
            throw new AuthException(StatusCodes.Status409Conflict, AuthErrorCodes.RoleLocked,
                "Yalnızca Yönetici ve Kullanıcı rolleri varsayılana döndürülebilir.");
        }

        var defaults = SystemRoles.DefaultPermissions[role.SystemKey];
        rules.EnsureGrantable(defaults, role.Permissions.Select(p => p.Permission));

        var before = AuditSnapshots.Role(role);
        RoleRules.ApplyPermissions(role, defaults);
        audit.Record(AuditActions.RoleReset, AuditEntities.Role, role.Id.ToString(), before, AuditSnapshots.Role(role));
        await db.SaveChangesAsync(ct);

        await changes.OnRoleDefinitionChangedAsync(role.Id, ct);
        return await rules.ToDtoAsync(role, ct);
    }
}
