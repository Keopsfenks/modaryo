using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Roles;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;

namespace IdentityService.Application.Companies.Commands;

/// <summary>
/// Üyenin şube erişimini rol kapsamının içinde daraltır. null daraltmayı kaldırır (rol kapsamı geçerli olur); boş liste geçersizdir.
/// Oturumdaki kapsam = rol kapsamı ∩ daraltma.
/// </summary>
[Authorize(Permission = Permissions.MembersManage)]
public record SetMemberBranchesCommand : IRequest
{
    public Guid UserId { get; init; }

    public Guid[]? BranchIds { get; init; }
}

public class SetMemberBranchesCommandHandler(IApplicationDbContext db, CompanyAccess access, RoleRules rules, MembershipChanges changes)
    : IRequestHandler<SetMemberBranchesCommand>
{
    public async Task Handle(SetMemberBranchesCommand request, CancellationToken ct)
    {
        var target = await access.MembershipAsync(request.UserId, ct);
        await db.Memberships.Entry(target).Collection(m => m.Branches).LoadAsync(ct);

        if (target.Role.SystemKey == SystemRoles.Owner)
        {
            await access.EnsureCallerIsOwnerAsync(ct);
        }

        if (request.BranchIds is { Length: 0 })
        {
            throw RoleRules.FieldError("branchIds", "En az bir şube seçin.");
        }

        var ids = (request.BranchIds ?? []).Distinct().ToArray();
        if (request.BranchIds is null)
        {
            // Daraltmayı kaldırmak üyeyi rolün tam kapsamına döndürür; çağıran bunu kendi kapsamıyla aşamaz (kendisi dahil).
            var role = await rules.LoadAsync(target.RoleId, ct);
            rules.EnsureRoleScopeWithinCallerScope(role);
        }
        else
        {
            var tenantId = access.TenantId;
            var valid = await db.Branches.AsNoTracking()
                .CountAsync(b => b.TenantId == tenantId && b.IsActive && ids.Contains(b.Id), ct);
            if (valid != ids.Length)
            {
                throw RoleRules.FieldError("branchIds", "Seçilen şubelerden biri bu firmaya ait değil veya pasif.");
            }

            rules.EnsureBranchesWithinCallerScope(ids);
        }

        target.BranchesRestricted = request.BranchIds is not null;
        target.Branches.RemoveAll(b => !ids.Contains(b.BranchId));
        var existing = target.Branches.Select(b => b.BranchId).ToHashSet();
        target.Branches.AddRange(ids.Where(b => !existing.Contains(b))
            .Select(b => new MembershipBranch { MembershipId = target.Id, BranchId = b }));
        await db.SaveChangesAsync(ct);

        await changes.OnRoleChangedAsync(target.UserId, target.TenantId, ct);
    }
}
