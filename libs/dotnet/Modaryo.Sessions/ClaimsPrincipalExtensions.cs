using System.Globalization;
using System.Security.Claims;

namespace Modaryo.Sessions;

public static class SessionClaimTypes
{
    public const string Permission = "permission";
    public const string TenantId = "tenant_id";
    public const string SessionId = "session_id";
    public const string SidHash = "sid_hash";
    public const string UserType = "user_type";
    public const string Module = "module";
    public const string Branch = "branch";
    public const string MaxDiscountPct = "max_discount_pct";
    public const string CariId = "cari_id";
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static Guid? GetTenantId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(SessionClaimTypes.TenantId), out var id) ? id : null;

    public static IReadOnlyList<string> GetPermissions(this ClaimsPrincipal principal) =>
        principal.FindAll(SessionClaimTypes.Permission).Select(c => c.Value).ToArray();

    /// <summary>"*" her izni karşılar; diğerleri birebir eşleşmelidir.</summary>
    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.FindAll(SessionClaimTypes.Permission)
            .Any(c => c.Value == SessionKeys.AllPermissions || c.Value == permission);

    public static string? GetSidHash(this ClaimsPrincipal principal) => principal.FindFirstValue(SessionClaimTypes.SidHash);

    public static string? GetSessionId(this ClaimsPrincipal principal) => principal.FindFirstValue(SessionClaimTypes.SessionId);

    public static string GetUserType(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(SessionClaimTypes.UserType) ?? SessionUserTypes.Employee;

    public static bool HasModule(this ClaimsPrincipal principal, string moduleKey) =>
        principal.FindAll(SessionClaimTypes.Module).Any(c => c.Value == moduleKey);

    /// <summary>Erişilebilen şube kimlikleri; null tüm şubeler demektir, boş liste hiçbir şube.</summary>
    public static IReadOnlyList<Guid>? GetBranchIds(this ClaimsPrincipal principal)
    {
        var values = principal.FindAll(SessionClaimTypes.Branch).Select(c => c.Value).ToArray();
        if (values.Contains(SessionKeys.AllBranches))
        {
            return null;
        }

        return values.Select(v => Guid.TryParse(v, out var id) ? id : (Guid?)null)
            .OfType<Guid>()
            .ToArray();
    }

    public static bool CanAccessBranch(this ClaimsPrincipal principal, Guid branchId) =>
        principal.GetBranchIds() is not { } ids || ids.Contains(branchId);

    public static decimal GetMaxDiscountPercent(this ClaimsPrincipal principal) =>
        decimal.TryParse(principal.FindFirstValue(SessionClaimTypes.MaxDiscountPct), NumberStyles.Number, CultureInfo.InvariantCulture, out var pct) ? pct : 0m;

    public static Guid? GetCariId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(SessionClaimTypes.CariId), out var id) ? id : null;
}
