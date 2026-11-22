using IdentityService.Application.Common.Interfaces;
using Microsoft.Extensions.Options;
using Modaryo.Sessions;

namespace IdentityService.Web.Infrastructure;

public sealed class SessionCookieOptions
{
    public const string Section = "Sessions";

    /// <summary>Üretimde zorunlu. Geliştirmede (http://localhost) appsettings.Development.json ile kapatılır.</summary>
    public bool SecureCookie { get; set; } = true;
}

/// <summary>"modaryo_sid" cookie'sini yazar/siler (spec 6.1).</summary>
public sealed class SessionCookie(IOptions<SessionCookieOptions> options)
{
    public void Write(HttpResponse response, CreatedSession session)
    {
        var cookie = BaseOptions();
        if (session.Record.RememberMe)
        {
            cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(session.Record.AbsoluteExpiresAt);
        }

        response.Cookies.Append(SessionKeys.CookieName, session.Sid, cookie);
    }

    public void Clear(HttpResponse response) => response.Cookies.Delete(SessionKeys.CookieName, BaseOptions());

    private CookieOptions BaseOptions() => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Secure = options.Value.SecureCookie,
        IsEssential = true,
    };
}
