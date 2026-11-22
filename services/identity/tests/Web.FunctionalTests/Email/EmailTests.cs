using System.Text.Json;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IdentityService.Web.FunctionalTests.Email;

public class EmailTests : TestBase
{
    private static IEmailComposer Composer => Factory.Services.GetRequiredService<IEmailComposer>();

    [Test]
    public void ConfirmEmail_ContainsLinkWithEncodedToken()
    {
        var userId = Guid.CreateVersion7();

        var message = Composer.ConfirmEmail("ali@ornek.com", "Ali Veli", userId, "a+b/c=");

        message.To.ShouldBe("ali@ornek.com");
        message.Subject.ShouldBe("Modaryo: e-posta adresinizi doğrulayın");
        var link = FakeEmailSender.ExtractLink(message);
        link.ShouldStartWith("http://localhost:3000/eposta-dogrula?");
        link.ShouldContain("a%2Bb%2Fc%3D");
        FakeEmailSender.QueryValue(link, "u").ShouldBe(userId.ToString());
        FakeEmailSender.QueryValue(link, "t").ShouldBe("a+b/c=");
        message.HtmlBody.ShouldContain("Ali Veli");
    }

    [Test]
    public void ResetPassword_LinkCarriesEmailAndToken()
    {
        var message = Composer.ResetPassword("ali+test@ornek.com", "Ali", "tok=en");

        message.Subject.ShouldBe("Modaryo: şifre sıfırlama");
        var link = FakeEmailSender.ExtractLink(message);
        link.ShouldStartWith("http://localhost:3000/sifre-sifirla?");
        FakeEmailSender.QueryValue(link, "e").ShouldBe("ali+test@ornek.com");
        FakeEmailSender.QueryValue(link, "t").ShouldBe("tok=en");
    }

    [Test]
    public void AccountAlreadyExists_PointsToLoginAndReset()
    {
        var message = Composer.AccountAlreadyExists("ali@ornek.com");

        message.Subject.ShouldBe("Modaryo: zaten bir hesabınız var");
        message.TextBody.ShouldContain("http://localhost:3000/giris");
        message.TextBody.ShouldContain("http://localhost:3000/sifremi-unuttum");
    }

    [Test]
    public void Invitation_ContainsCompanyNameAndLink()
    {
        var message = Composer.Invitation("veli@ornek.com", "Akdeniz Tekstil", "Ali Veli", "abc_DEF-123");

        message.Subject.ShouldBe("Modaryo: Akdeniz Tekstil sizi davet etti");
        FakeEmailSender.ExtractLink(message).ShouldBe("http://localhost:3000/davet/abc_DEF-123");
        message.TextBody.ShouldContain("Ali Veli");
    }

    [Test]
    public void Invitation_EscapesHtmlInCompanyName()
    {
        var message = Composer.Invitation("veli@ornek.com", "<script>x</script>", "Ali", "tok");

        message.HtmlBody.ShouldNotContain("<script>");
        message.HtmlBody.ShouldContain("&lt;script&gt;");
    }

    [Test]
    public void CleanupReminder_MentionsDeletionDate()
    {
        var message = Composer.CleanupReminder("ali@ornek.com", "Ali", new DateTimeOffset(2026, 12, 21, 0, 0, 0, TimeSpan.Zero));

        message.Subject.ShouldBe("Modaryo: hesabınız yakında silinecek");
        message.TextBody.ShouldContain("21.12.2026");
        message.TextBody.ShouldContain("http://localhost:3000/baslangic");
    }

    [Test]
    public async Task Smtp_DeliversToMailpit()
    {
        var sender = new SmtpEmailSender(Options.Create(new SmtpOptions
        {
            Host = "localhost",
            Port = 1025,
            From = "Modaryo <no-reply@modaryo.local>",
        }));
        var to = $"test-{Guid.NewGuid():N}@test.local";

        await sender.SendAsync(new EmailMessage(to, "Modaryo test", "<p>merhaba</p>", "merhaba"), CancellationToken.None);

        using var http = new HttpClient();
        var json = await http.GetStringAsync($"http://localhost:8025/api/v1/search?query=to:{Uri.EscapeDataString(to)}");
        JsonDocument.Parse(json).RootElement.GetProperty("messages_count").GetInt32().ShouldBe(1);
    }
}
