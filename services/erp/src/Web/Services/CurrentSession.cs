using ERP.Application.Common.Interfaces;
using Modaryo.Sessions;

namespace ERP.Web.Services;

/// <summary>Oturum bilgisini Modaryo.Sessions kimlik doğrulamasının ürettiği claim'lerden okur.</summary>
public sealed class CurrentSession(
    IHttpContextAccessor accessor
) : ICurrentSession, ICurrentTenant {
    private HttpContext? Context => accessor.HttpContext;

    public Guid? UserId => Context?.User.GetUserId();

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