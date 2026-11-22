using System.Globalization;
using System.Text.Encodings.Web;
using IdentityService.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace IdentityService.Infrastructure.Email;

public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>E-posta linklerinin gideceği frontend adresi.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:3000";
}

/// <summary>Türkçe e-posta şablonları. Kullanıcıdan gelen metinler HTML'de kaçışlanır.</summary>
public sealed class EmailComposer(IOptions<AppOptions> options) : IEmailComposer
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private string BaseUrl => options.Value.FrontendBaseUrl.TrimEnd('/');

    public EmailMessage ConfirmEmail(string to, string fullName, Guid userId, string token)
    {
        var link = $"{BaseUrl}/eposta-dogrula?u={userId:D}&t={Uri.EscapeDataString(token)}";
        return Build(to, "Modaryo: e-posta adresinizi doğrulayın",
            $"Merhaba {fullName},",
            "Modaryo hesabınızı etkinleştirmek için e-posta adresinizi doğrulayın. Bağlantı 48 saat geçerlidir.",
            "E-postamı doğrula", link,
            "Bu kaydı siz yapmadıysanız bu e-postayı yok sayabilirsiniz.");
    }

    public EmailMessage ResetPassword(string to, string fullName, string token)
    {
        var link = $"{BaseUrl}/sifre-sifirla?e={Uri.EscapeDataString(to)}&t={Uri.EscapeDataString(token)}";
        return Build(to, "Modaryo: şifre sıfırlama",
            $"Merhaba {fullName},",
            "Şifrenizi sıfırlamak için aşağıdaki bağlantıyı kullanın. Bağlantı 1 saat geçerlidir.",
            "Şifremi sıfırla", link,
            "Bu isteği siz yapmadıysanız şifreniz değişmedi; bu e-postayı yok sayabilirsiniz.");
    }

    public EmailMessage AccountAlreadyExists(string to) =>
        Build(to, "Modaryo: zaten bir hesabınız var",
            "Merhaba,",
            "Bu e-posta adresiyle yeni bir kayıt denendi, ancak adresinize ait bir Modaryo hesabı zaten var. Giriş yapabilir "
            + $"ya da şifrenizi unuttuysanız sıfırlayabilirsiniz: {BaseUrl}/sifremi-unuttum",
            "Giriş yap", $"{BaseUrl}/giris",
            "Bu kaydı siz denemediyseniz herhangi bir işlem yapmanıza gerek yok.");

    public EmailMessage Invitation(string to, string companyName, string inviterName, string inviteToken) =>
        Build(to, $"Modaryo: {companyName} sizi davet etti",
            "Merhaba,",
            $"{inviterName}, sizi Modaryo'da {companyName} firmasına davet etti. Davet 7 gün geçerlidir.",
            "Daveti görüntüle", $"{BaseUrl}/davet/{inviteToken}",
            "Bu daveti beklemiyorsanız e-postayı yok sayabilirsiniz.");

    public EmailMessage CleanupReminder(string to, string fullName, DateTimeOffset deleteOn) =>
        Build(to, "Modaryo: hesabınız yakında silinecek",
            $"Merhaba {fullName},",
            $"Hesabınız henüz bir firmaya bağlı değil. {deleteOn.ToString("dd.MM.yyyy", Turkish)} tarihine kadar bir firma "
            + "oluşturmaz ya da bir davete katılmazsanız hesabınız silinecek.",
            "Firma oluştur veya davete katıl", $"{BaseUrl}/baslangic",
            "Hesabınızı kullanmak istemiyorsanız herhangi bir işlem yapmanıza gerek yok.");

    private static EmailMessage Build(string to, string subject, string greeting, string body, string buttonText, string link, string footer)
    {
        var html = HtmlEncoder.Default;
        var htmlBody = $"""
            <!doctype html>
            <html lang="tr">
            <body style="margin:0;padding:24px;background:#f2f6fa;font-family:Arial,sans-serif;color:#2a3547;">
              <div style="max-width:520px;margin:0 auto;background:#fff;border-radius:12px;padding:32px;">
                <div style="font-size:20px;font-weight:700;color:#5d87ff;margin-bottom:24px;">Modaryo</div>
                <p style="margin:0 0 12px;">{html.Encode(greeting)}</p>
                <p style="margin:0 0 24px;line-height:1.5;">{html.Encode(body)}</p>
                <a href="{html.Encode(link)}" style="display:inline-block;background:#5d87ff;color:#fff;text-decoration:none;padding:12px 20px;border-radius:7px;font-weight:600;">{html.Encode(buttonText)}</a>
                <p style="margin:24px 0 0;font-size:12px;color:#5a6a85;word-break:break-all;">Buton çalışmazsa bu adresi tarayıcınıza yapıştırın:<br>{html.Encode(link)}</p>
                <p style="margin:16px 0 0;font-size:12px;color:#5a6a85;">{html.Encode(footer)}</p>
              </div>
            </body>
            </html>
            """;

        var textBody = $"{greeting}\n\n{body}\n\n{buttonText}: {link}\n\n{footer}\n";
        return new EmailMessage(to, subject, htmlBody, textBody);
    }
}
