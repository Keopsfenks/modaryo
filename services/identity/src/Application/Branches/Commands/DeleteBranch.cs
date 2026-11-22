using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;

namespace IdentityService.Application.Branches.Commands;

/// <summary>
/// Şubeyi ve rol/üye şube kayıtlarını siler, firmanın açık oturumlarını yeniler.
/// ERP belgelerinde kullanım denetimi P0b'de eklenecek; o zamana dek silme serbesttir.
/// </summary>
[Authorize(Permission = Permissions.SettingsBranches)]
public record DeleteBranchCommand(Guid Id) : IRequest;

public class DeleteBranchCommandHandler(IApplicationDbContext db, CompanyAccess access, BranchRules rules, MembershipChanges changes)
    : IRequestHandler<DeleteBranchCommand>
{
    public async Task Handle(DeleteBranchCommand request, CancellationToken ct)
    {
        rules.EnsureCallerHasAllBranches();
        var branch = await rules.LoadAsync(request.Id, ct);
        if (branch.IsDefault)
        {
            throw BranchRules.DefaultBranch("Varsayılan şube silinemez.");
        }

        var tenantId = access.TenantId;
        var roles = await db.TenantRoles.Include(r => r.Branches)
            .Where(r => r.TenantId == tenantId && r.Branches.Any(b => b.BranchId == branch.Id))
            .ToListAsync(ct);
        foreach (var role in roles)
        {
            role.Branches.RemoveAll(b => b.BranchId == branch.Id);
        }

        var memberships = await db.Memberships.Include(m => m.Branches)
            .Where(m => m.TenantId == tenantId && m.Branches.Any(b => b.BranchId == branch.Id))
            .ToListAsync(ct);
        foreach (var membership in memberships)
        {
            membership.Branches.RemoveAll(b => b.BranchId == branch.Id);
        }

        db.Branches.Remove(branch);
        await db.SaveChangesAsync(ct);

        await changes.RefreshTenantAsync(tenantId, ct);
    }
}
