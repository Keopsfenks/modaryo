namespace IdentityService.Application.Companies;

public sealed record CompanyDto(Guid Id, string Name, DateTimeOffset CreatedAt);

public sealed record RoleDto(Guid Id, string Name, string? SystemKey);

public sealed record MemberDto(Guid UserId, string Email, string FullName, Guid RoleId, string RoleName, string? RoleSystemKey, DateTimeOffset JoinedAt);
