using IdentityService.Application.Audit;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;

namespace IdentityService.Application.Companies.Commands;

/// <summary>Firma + hazır roller oluşur, çağıran sahip olur ve oturumun aktif firması bu firma olur.</summary>
[Authorize]
public record CreateCompanyCommand : IRequest<CompanyDto>
{
    public string Name { get; init; } = string.Empty;
}


public class CreateCompanyCommandValidator : AbstractValidator<CreateCompanyCommand>
{
    public CreateCompanyCommandValidator() => RuleFor(c => c.Name).ValidCompanyName();
}

public class CreateCompanyCommandHandler(IApplicationDbContext db, ICurrentSession current, MembershipChanges changes, TimeProvider time)
    : IRequestHandler<CreateCompanyCommand, CompanyDto>
{
    public async Task<CompanyDto> Handle(CreateCompanyCommand request, CancellationToken ct)
    {
        var userId = current.UserId!.Value;
        var now = time.GetUtcNow();

        var tenant = new Tenant
        {
            Name = request.Name.Trim(),
            CreatedAt = now,
            CreatedByUserId = userId,
            AppliedPermissionSetVersion = SystemRoles.LatestPermissionSetVersion,
        };
        var roles = SystemRoles.CreateDefaults(tenant.Id);

        db.Tenants.Add(tenant);
        db.Branches.Add(new Branch { TenantId = tenant.Id, Code = "MRK", Name = "Merkez", IsDefault = true, CreatedAt = now });
        db.TenantRoles.AddRange(roles);
        db.Memberships.Add(new Membership
        {
            TenantId = tenant.Id,
            UserId = userId,
            RoleId = roles.Single(r => r.SystemKey == SystemRoles.Owner).Id,
            JoinedAt = now,
        });
        await db.SaveChangesAsync(ct);

        await changes.OnJoinedAsync(userId, tenant.Id, current.SidHash, ct);
        return new CompanyDto(tenant.Id, tenant.Name, tenant.CreatedAt);
    }
}

[Authorize(Permission = Permissions.CompanyUpdate)]
public record UpdateCompanyCommand : IRequest
{
    public string Name { get; init; } = string.Empty;
}

public class UpdateCompanyCommandValidator : AbstractValidator<UpdateCompanyCommand>
{
    public UpdateCompanyCommandValidator() => RuleFor(c => c.Name).ValidCompanyName();
}

public class UpdateCompanyCommandHandler(IApplicationDbContext db, CompanyAccess access) : IRequestHandler<UpdateCompanyCommand>
{
    public async Task Handle(UpdateCompanyCommand request, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstAsync(t => t.Id == access.TenantId, ct);
        tenant.Name = request.Name.Trim();
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Aktif firmadan ayrılır. Son sahip ayrılamaz.</summary>
[Authorize]
public record LeaveCompanyCommand : IRequest;

public class LeaveCompanyCommandHandler(IApplicationDbContext db, CompanyAccess access, MembershipChanges changes) : IRequestHandler<LeaveCompanyCommand>
{
    public async Task Handle(LeaveCompanyCommand request, CancellationToken ct)
    {
        var membership = await access.MembershipAsync(access.UserId, ct);
        await access.EnsureNotLastOwnerAsync(membership, ct);

        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(ct);
        await changes.OnLeftAsync(membership.UserId, membership.TenantId, ct);
    }
}

[Authorize(Permission = Permissions.MembersManage)]
public record ChangeMemberRoleCommand : IRequest
{
    public Guid UserId { get; init; }

    public Guid RoleId { get; init; }
}

public class ChangeMemberRoleCommandHandler(IApplicationDbContext db, CompanyAccess access, MembershipChanges changes, Roles.RoleRules roleRules, IAuditLog audit)
    : IRequestHandler<ChangeMemberRoleCommand>
{
    public async Task Handle(ChangeMemberRoleCommand request, CancellationToken ct)
    {
        var target = await access.MembershipAsync(request.UserId, ct);
        var newRole = await roleRules.LoadAsync(request.RoleId, ct);

        if (target.RoleId == newRole.Id)
        {
            // Değişiklik yok: yazma ve denetim kaydı atlanır, ama önceki başarısız yenileme tekrar denemeyle onarılabilsin.
            await changes.OnRoleChangedAsync(target.UserId, target.TenantId, ct);
            return;
        }

        if (newRole.SystemKey == SystemRoles.Owner || target.Role.SystemKey == SystemRoles.Owner)
        {
            await access.EnsureCallerIsOwnerAsync(ct);
        }

        roleRules.EnsureRoleAssignable(newRole);
        await access.EnsureNotLastOwnerAsync(target, ct);

        audit.Record(AuditActions.MemberRoleChanged, AuditEntities.Member, target.UserId.ToString(),
            AuditSnapshots.MemberRole(target.RoleId, target.Role.Name), AuditSnapshots.MemberRole(newRole.Id, newRole.Name));
        target.RoleId = newRole.Id;
        await db.SaveChangesAsync(ct);
        await changes.OnRoleChangedAsync(target.UserId, target.TenantId, ct);
    }
}

[Authorize(Permission = Permissions.MembersManage)]
public record RemoveMemberCommand(Guid UserId) : IRequest;

public class RemoveMemberCommandHandler(IApplicationDbContext db, CompanyAccess access, MembershipChanges changes, IAuditLog audit)
    : IRequestHandler<RemoveMemberCommand>
{
    public async Task Handle(RemoveMemberCommand request, CancellationToken ct)
    {
        var target = await access.MembershipAsync(request.UserId, ct);

        if (target.Role.SystemKey == SystemRoles.Owner)
        {
            await access.EnsureCallerIsOwnerAsync(ct);
        }

        await access.EnsureNotLastOwnerAsync(target, ct);

        audit.Record(AuditActions.MemberRemoved, AuditEntities.Member, target.UserId.ToString(),
            AuditSnapshots.MemberRole(target.RoleId, target.Role.Name), null);
        db.Memberships.Remove(target);
        await db.SaveChangesAsync(ct);
        await changes.OnLeftAsync(target.UserId, target.TenantId, ct);
    }
}
