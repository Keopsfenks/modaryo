using IdentityService.Application.Auth;
using IdentityService.Application.Auth.Commands.ConfirmEmail;
using IdentityService.Application.Auth.Commands.ForgotPassword;
using IdentityService.Application.Auth.Commands.ResetPassword;
using IdentityService.Application.Auth.Commands.Login;
using IdentityService.Application.Auth.Commands.Logout;
using IdentityService.Application.Auth.Commands.Register;
using IdentityService.Application.Auth.Commands.ResendConfirmation;
using IdentityService.Application.Auth.Commands.SwitchTenant;
using IdentityService.Application.Auth.Queries.GetSession;
using IdentityService.Web.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IdentityService.Web.Endpoints;

public class Auth : IEndpointGroup
{
    public static string? RoutePrefix => "/api/identity/auth";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(Register, "register").RequireRateLimiting(RateLimiting.Register);
        groupBuilder.MapPost(ConfirmEmail, "confirm-email");
        groupBuilder.MapPost(ResendConfirmation, "resend-confirmation").RequireRateLimiting(RateLimiting.EmailSensitive);
        groupBuilder.MapPost(Login, "login").RequireRateLimiting(RateLimiting.Login);
        groupBuilder.MapPost(Logout, "logout");
        groupBuilder.MapPost(LogoutAll, "logout-all");
        groupBuilder.MapGet(GetSession, "session");
        groupBuilder.MapPost(SwitchTenant, "session/tenant");
        groupBuilder.MapPost(ForgotPassword, "forgot-password").RequireRateLimiting(RateLimiting.EmailSensitive);
        groupBuilder.MapPost(ResetPassword, "reset-password").RequireRateLimiting(RateLimiting.EmailSensitive);
    }

    [EndpointSummary("Şifremi unuttum")]
    [EndpointDescription("Hesap varsa sıfırlama e-postası gönderir. Yanıt her durumda 202'dir.")]
    public static async Task<Accepted> ForgotPassword(ISender sender, ForgotPasswordCommand command)
    {
        await sender.Send(command);
        return TypedResults.Accepted((string?)null);
    }

    [EndpointSummary("Şifreyi sıfırla")]
    [EndpointDescription("E-postadaki kodla yeni şifre belirler; kullanıcının tüm oturumları kapanır. Geçersiz kod: 400 invalid_token.")]
    public static async Task<NoContent> ResetPassword(ISender sender, ResetPasswordCommand command)
    {
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Kayıt ol")]
    [EndpointDescription("Yeni hesap açar ve doğrulama e-postası gönderir. E-posta zaten kayıtlıysa yanıt aynıdır; o adrese bilgilendirme e-postası gider.")]
    public static async Task<Results<Accepted, Ok<SessionInfo>>> Register(ISender sender, SessionCookie cookie, HttpResponse response, RegisterCommand command)
    {
        var issued = await sender.Send(command);
        if (issued is null)
        {
            return TypedResults.Accepted((string?)null);
        }

        // Davet linkiyle kayıt: hesap doğrulanmış açıldı, davet kabul edildi, oturum açıldı.
        cookie.Write(response, issued.Created);
        return TypedResults.Ok(issued.Info);
    }

    [EndpointSummary("E-postayı doğrula")]
    [EndpointDescription("E-postadaki bağlantıdaki kullanıcı kimliği ve kodla hesabı doğrular ve oturum açar.")]
    public static async Task<Ok<SessionInfo>> ConfirmEmail(ISender sender, SessionCookie cookie, HttpResponse response, ConfirmEmailCommand command)
    {
        var issued = await sender.Send(command);
        cookie.Write(response, issued.Created);
        return TypedResults.Ok(issued.Info);
    }

    [EndpointSummary("Doğrulama e-postasını yeniden gönder")]
    public static async Task<Accepted> ResendConfirmation(ISender sender, ResendConfirmationCommand command)
    {
        await sender.Send(command);
        return TypedResults.Accepted((string?)null);
    }

    [EndpointSummary("Giriş yap")]
    [EndpointDescription("Başarılıysa 'modaryo_sid' cookie'sini yazar. 401 invalid_credentials, 403 email_not_confirmed, 423 account_locked, 429 rate_limited.")]
    public static async Task<Ok<SessionInfo>> Login(ISender sender, SessionCookie cookie, HttpResponse response, LoginCommand command)
    {
        var issued = await sender.Send(command);
        cookie.Write(response, issued.Created);
        return TypedResults.Ok(issued.Info);
    }

    [EndpointSummary("Çıkış yap")]
    public static async Task<NoContent> Logout(ISender sender, SessionCookie cookie, HttpResponse response)
    {
        await sender.Send(new LogoutCommand());
        cookie.Clear(response);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Tüm cihazlardan çıkış yap")]
    public static async Task<NoContent> LogoutAll(ISender sender, SessionCookie cookie, HttpResponse response)
    {
        await sender.Send(new LogoutAllCommand());
        cookie.Clear(response);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Oturum bilgisi")]
    [EndpointDescription("Kullanıcı, aktif firma, izinler ve üye olunan firmalar. Oturum yoksa 401.")]
    public static async Task<Ok<SessionInfo>> GetSession(ISender sender) =>
        TypedResults.Ok(await sender.Send(new GetSessionQuery()));

    [EndpointSummary("Aktif firmayı değiştir")]
    public static async Task<Ok<SessionInfo>> SwitchTenant(ISender sender, SwitchTenantCommand command) =>
        TypedResults.Ok(await sender.Send(command));
}
