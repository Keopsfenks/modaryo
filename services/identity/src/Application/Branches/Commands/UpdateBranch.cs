using IdentityService.Application.Common.Interfaces;
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

public class UpdateBranchCommandHandler(IApplicationDbContext db, BranchRules rules) : IRequestHandler<UpdateBranchCommand, BranchDto>
{
    public async Task<BranchDto> Handle(UpdateBranchCommand request, CancellationToken ct)
    {
        var branch = await rules.LoadAsync(request.Id, ct);
        if (branch.IsDefault && !request.IsActive)
        {
            throw BranchRules.DefaultBranch("Varsayılan şube pasif yapılamaz.");
        }

        var code = BranchRules.NormalizeCode(request.Code);
        await rules.EnsureCodeFreeAsync(code, branch.Id, ct);

        branch.Code = code;
        branch.Name = request.Name.Trim();
        branch.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return BranchRules.ToDto(branch);
    }
}
