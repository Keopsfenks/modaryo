namespace IdentityService.Application.Auth;

public sealed record UserDto(Guid Id, string Email, string FullName);

public sealed record CompanyRef(Guid Id, string Name, string RoleName);

/// <summary>Frontend'in oturum hakkında bildiği her şey (GET /auth/session ve giriş yanıtları).</summary>
public sealed record SessionInfo(
    UserDto User,
    CompanyRef? ActiveCompany,
    string[] Permissions,
    CompanyRef[] Companies,
    string[] Modules,
    Guid[]? BranchIds,
    decimal MaxDiscountPercent,
    string UserType);

/// <summary>Yeni açılmış oturum: <see cref="Created"/> cookie'ye yazılır, <see cref="Info"/> yanıt gövdesidir.</summary>
public sealed record IssuedSession(Common.Interfaces.CreatedSession Created, SessionInfo Info);
