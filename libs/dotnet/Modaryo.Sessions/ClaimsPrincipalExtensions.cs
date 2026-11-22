using System.Security.Claims;

namespace Modaryo.Sessions;

public static class SessionClaimTypes
{
    public const string Permission = "permission";
    public const string TenantId = "tenant_id";
    public const string SessionId = "session_id";
    public const string SidHash = "sid_hash";
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
}
