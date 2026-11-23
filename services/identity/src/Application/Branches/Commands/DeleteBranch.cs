using IdentityService.Application.Audit;
using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Branches.Commands;

/// <summary>
/// Şubeyi ve rol/üye şube kayıtlarını siler, firmanın açık oturumlarını yeniler.
/// Silmeden önce ERP'ye şubenin kullanımı sorulur: kullanımdaysa 409 branch_in_use; ERP'ye ulaşılamazsa 503 erp_unavailable (güvenli tarafta kalır).
/// </summary>
[Authorize(Permission = Permissions.SettingsBranches)]
public record DeleteBranchCommand(Guid Id) : IRequest;

public class DeleteBranchCommandHandler(IApplicationDbContext db, CompanyAccess access, BranchRules rules, MembershipChanges changes, IAuditLog audit, IErpBranchUsageClient erp)
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
        if (await erp.IsInUseAsync(tenantId, branch.Id, ct))
        {
            throw new AuthException(StatusCodes.Status409Conflict, AuthErrorCodes.BranchInUse, "Bu şube ERP kayıtlarında kullanılıyor; silinemez.");
        }

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

        audit.Record(AuditActions.BranchDeleted, AuditEntities.Branch, branch.Id.ToString(), AuditSnapshots.Branch(branch), null);
        db.Branches.Remove(branch);
        await db.SaveChangesAsync(ct);

        await changes.RefreshTenantAsync(tenantId, ct);
    }
}
