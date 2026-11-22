using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Modaryo.Sessions;

public static class SessionAuthenticationDefaults {
    public const string Scheme = "ModaryoSession";
}

public sealed class SessionAuthenticationOptions : AuthenticationSchemeOptions {
    /// <summary>Bu serviste kabul edilen kullanıcı türleri; varsayılan olarak yalnızca çalışan.</summary>
    public ISet<string> AllowedUserTypes { get; } = new HashSet<string> { SessionUserTypes.Employee };
}

/// <summary>
///     "modaryo_sid" cookie'sini Valkey'deki kayda çevirir. Cookie yoksa veya kayıt geçersizse
///     NoResult döner; 401/403 yetki katmanında üretilir.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<SessionAuthenticationOptions> options,
    ILoggerFactory                                logger,
    UrlEncoder                                    encoder,
    SessionReader                                 reader
)
    : AuthenticationHandler<SessionAuthenticationOptions>(options, logger, encoder) {
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
        if (!Request.Cookies.TryGetValue(SessionKeys.CookieName, out var sid) || string.IsNullOrWhiteSpace(sid))
            return AuthenticateResult.NoResult();

        var lookup = await reader.ReadAsync(sid, Context.RequestAborted);
        if (lookup is null) return AuthenticateResult.NoResult();

        var record = lookup.Record;
        if (!SessionUserTypes.IsKnown(record.UserType) || !Options.AllowedUserTypes.Contains(record.UserType))
            return AuthenticateResult.NoResult();

        var claims = new List<Claim> {
                                         new(ClaimTypes.NameIdentifier, record.UserId.ToString("D")),
                                         new(ClaimTypes.Email, record.Email),
                                         new(ClaimTypes.Name, record.FullName),
                                         new(SessionClaimTypes.SessionId, record.Id),
                                         new(SessionClaimTypes.SidHash, lookup.SidHash),
                                         new(SessionClaimTypes.UserType, record.UserType),
                                         new(SessionClaimTypes.MaxDiscountPct,
                                             record.MaxDiscountPct.ToString(CultureInfo.InvariantCulture))
                                     };

        if (record.CariId is { } cariId) claims.Add(new Claim(SessionClaimTypes.CariId, cariId.ToString("D")));

        claims.AddRange(record.Modules.Select(m => new Claim(SessionClaimTypes.Module, m)));

        if (record.BranchIds is null)
            claims.Add(new Claim(SessionClaimTypes.Branch, SessionKeys.AllBranches));
        else
            claims.AddRange(record.BranchIds.Select(b => new Claim(SessionClaimTypes.Branch, b.ToString("D"))));

        if (record.TenantId is { } tenantId) claims.Add(new Claim(SessionClaimTypes.TenantId, tenantId.ToString("D")));

        claims.AddRange(record.Permissions.Select(p => new Claim(SessionClaimTypes.Permission, p)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}