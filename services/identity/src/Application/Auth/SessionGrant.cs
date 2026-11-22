using IdentityService.Domain.Constants;

namespace IdentityService.Application.Auth;

/// <summary>
/// Oturum kaydına yazılan, aktif firmaya özgü yetki paketi. <see cref="BranchIds"/> null ise tüm şubeler,
/// boş dizi ise hiçbir şube demektir.
/// </summary>
public sealed record SessionGrant(
    Guid? TenantId,
    string[] Permissions,
    string[] Modules,
    Guid[]? BranchIds,
    decimal MaxDiscountPct,
    string UserType,
    Guid? CariId)
{
    /// <summary>Aktif firması olmayan oturum.</summary>
    public static SessionGrant None { get; } = new(null, [], [], null, 0m, MembershipKinds.Employee, null);
}
