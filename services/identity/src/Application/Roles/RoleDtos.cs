namespace IdentityService.Application.Roles;

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? SystemKey,
    bool Locked,
    string[] Permissions,
    decimal MaxDiscountPercent,
    bool AllBranches,
    Guid[] BranchIds,
    int MemberCount);
