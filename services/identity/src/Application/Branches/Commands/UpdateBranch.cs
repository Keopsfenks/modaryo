using IdentityService.Application.Audit;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Companies;
using IdentityService.Application.Common.Security;
using IdentityService.Domain.Constants;

namespace IdentityService.Application.Branches.Commands;

[Authorize(Permission = Permissions.SettingsBranches)]
public record UpdateBranchCommand : IRequest<BranchDto>
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;
}

public class UpdateBranchCommandValidator : AbstractValidator<UpdateBranchCommand>
{
    public UpdateBranchCommandValidator()
    {
        BranchRules.ValidCode(RuleFor(c => c.Code));
        BranchRules.ValidName(RuleFor(c => c.Name));
    }
}

public class UpdateBranchCommandHandler(BranchRules rules, CompanyAccess access, MembershipChanges changes, IAuditLog audit) : IRequestHandler<UpdateBranchCommand, BranchDto>
{
    public async Task<BranchDto> Handle(UpdateBranchCommand request, CancellationToken ct)
    {
        rules.EnsureCallerHasAllBranches();
        var branch = await rules.LoadAsync(request.Id, ct);
        if (branch.IsDefault && !request.IsActive)
        {
            throw BranchRules.DefaultBranch("Varsayılan şube pasif yapılamaz.");
        }

        var code = BranchRules.NormalizeCode(request.Code);
        await rules.EnsureCodeFreeAsync(code, branch.Id, ct);

        var activeChanged = branch.IsActive != request.IsActive;
        var before = AuditSnapshots.Branch(branch);
        branch.Code = code;
        branch.Name = request.Name.Trim();
        branch.IsActive = request.IsActive;
        audit.Record(AuditActions.BranchUpdated, AuditEntities.Branch, branch.Id.ToString(), before, AuditSnapshots.Branch(branch));
        await rules.SaveCodeChangeAsync(code, branch.Id, ct);

        if (activeChanged)
        {
            // Pasif şube hiçbir oturumda erişim sağlamaz; açık oturumlar hemen yenilenir.
            await changes.RefreshTenantAsync(access.TenantId, ct);
        }

        return BranchRules.ToDto(branch);
    }
}
