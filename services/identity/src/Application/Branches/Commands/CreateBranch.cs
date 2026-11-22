using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;

namespace IdentityService.Application.Branches.Commands;

[Authorize(Permission = Permissions.SettingsBranches)]
public record CreateBranchCommand : IRequest<BranchDto>
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}

public class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchCommandValidator()
    {
        BranchRules.ValidCode(RuleFor(c => c.Code));
        BranchRules.ValidName(RuleFor(c => c.Name));
    }
}

public class CreateBranchCommandHandler(IApplicationDbContext db, CompanyAccess access, BranchRules rules, TimeProvider time)
    : IRequestHandler<CreateBranchCommand, BranchDto>
{
    public async Task<BranchDto> Handle(CreateBranchCommand request, CancellationToken ct)
    {
        var code = BranchRules.NormalizeCode(request.Code);
        await rules.EnsureCodeFreeAsync(code, null, ct);

        var branch = new Branch
        {
            TenantId = access.TenantId,
            Code = code,
            Name = request.Name.Trim(),
            CreatedAt = time.GetUtcNow(),
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);
        return BranchRules.ToDto(branch);
    }
}
