using System.Security.Claims;
using ERP.Application.Common.Interfaces;
using Modaryo.Sessions;

namespace ERP.Web.Services;

/// <summary>Oturum bilgisini Modaryo.Sessions kimlik doğrulamasının ürettiği claim'lerden okur.</summary>
public sealed class CurrentSession(
    IHttpContextAccessor accessor
) : ICurrentSession, ICurrentTenant {
    private HttpContext? Context => accessor.HttpContext;

    public Guid? UserId => Context?.User.GetUserId();

    public string? UserFullName => Context?.User.FindFirstValue(ClaimTypes.Name);

    public bool HasModule(string key) {
        return Context?.User.HasModule(key) ?? false;
    }

    /// <summary>HTTP bağlamı yoksa boş liste: hiçbir şubeye erişim yok (güvenli varsayılan).</summary>
    public IReadOnlyList<Guid>? BranchIds => Context is null ? [] : Context.User.GetBranchIds();

    public bool CanAccessBranch(Guid branchId) {
        return BranchIds is not { } ids || ids.Contains(branchId);
    }

    public decimal MaxDiscountPercent => Context?.User.GetMaxDiscountPercent() ?? 0m;

    public Guid? TenantId => Context?.User.GetTenantId();

    public string? SidHash => Context?.User.GetSidHash();

    public string? SessionId => Context?.User.GetSessionId();

    public IReadOnlyList<string> Permissions => Context?.User.GetPermissions() ?? [];

    public bool HasPermission(string permission) {
        return Context?.User.HasPermission(permission) ?? false;
    }

    /// <summary>Proxy arkasında UseForwardedHeaders sonrası gerçek istemci adresi.</summary>
    public string? Ip => Context?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent {
        get {
            var value = Context?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, 512)];
        }
    }
}