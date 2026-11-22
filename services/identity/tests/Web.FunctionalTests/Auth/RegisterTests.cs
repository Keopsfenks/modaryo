using IdentityService.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Auth;

public class RegisterTests : TestBase
{
    private const string Password = "guvenli-sifre-123";

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password = Password, string fullName = "Ali Veli") =>
        client.PostAsJsonAsync("/api/identity/auth/register", new { email, password, fullName });

    [Test]
    public async Task Register_NewEmail_Returns202AndSendsConfirmation()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await RegisterAsync(client, "ali@ornek.com");

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.SessionCookieHeader().ShouldBeNull();
        Factory.Emails.LastTo("ali@ornek.com").Subject.ShouldBe("Modaryo: e-posta adresinizi doğrulayın");
    }

    [Test]
    public async Task Register_ExistingEmail_Returns202AndSendsAlreadyExists()
    {
        using var client = Factory.CreateClientWithCookies();
        await RegisterAsync(client, "ali@ornek.com");

        using var second = await RegisterAsync(client, "ali@ornek.com", "baska-bir-sifre-456", "Başka Biri");

        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Factory.Emails.To("ali@ornek.com").Select(m => m.Subject).ShouldBe(
            ["Modaryo: e-posta adresinizi doğrulayın", "Modaryo: zaten bir hesabınız var"]);
        using var scope = Factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<IUserAccounts>().FindByEmailAsync("ali@ornek.com", CancellationToken.None);
        user!.FullName.ShouldBe("Ali Veli");
    }

    [Test]
    public async Task Register_ShortPassword_Returns400()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await RegisterAsync(client, "ali@ornek.com", "123456789");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Factory.Emails.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task Register_InvalidEmail_Returns400()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await RegisterAsync(client, "gecersiz-adres");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Register_NormalizesEmail()
    {
        using var client = Factory.CreateClientWithCookies();

        await RegisterAsync(client, "  Ali@Ornek.COM ");

        using var scope = Factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<IUserAccounts>().FindByEmailAsync("ali@ornek.com", CancellationToken.None);
        user.ShouldNotBeNull();
        user.Email.ShouldBe("ali@ornek.com");
        Factory.Emails.LastTo("ali@ornek.com").ShouldNotBeNull();
    }

    [Test]
    public async Task ConfirmEmail_ValidLink_SetsConfirmedAndLogsIn()
    {
        using var client = Factory.CreateClientWithCookies();
        await RegisterAsync(client, "ali@ornek.com");
        var link = FakeEmailSender.ExtractLink(Factory.Emails.LastTo("ali@ornek.com"));

        using var response = await client.PostAsJsonAsync("/api/identity/auth/confirm-email", new
        {
            userId = FakeEmailSender.QueryValue(link, "u"),
            token = FakeEmailSender.QueryValue(link, "t"),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cookie = response.SessionCookieHeader();
        cookie.ShouldNotBeNull();
        cookie.ToLowerInvariant().ShouldContain("httponly");
        cookie.ToLowerInvariant().ShouldContain("samesite=lax");
        cookie.ToLowerInvariant().ShouldContain("path=/");
        cookie.ToLowerInvariant().ShouldNotContain("expires="); // beni hatırla yok → tarayıcı oturumu cookie'si
        var body = await response.JsonAsync();
        body.GetProperty("user").GetProperty("email").GetString().ShouldBe("ali@ornek.com");
        body.GetProperty("activeCompany").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        using var scope = Factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<IUserAccounts>().FindByEmailAsync("ali@ornek.com", CancellationToken.None);
        user!.EmailConfirmed.ShouldBeTrue();
    }

    [Test]
    public async Task ConfirmEmail_TamperedToken_Returns400InvalidToken()
    {
        using var client = Factory.CreateClientWithCookies();
        await RegisterAsync(client, "ali@ornek.com");
        var link = FakeEmailSender.ExtractLink(Factory.Emails.LastTo("ali@ornek.com"));

        using var response = await client.PostAsJsonAsync("/api/identity/auth/confirm-email", new
        {
            userId = FakeEmailSender.QueryValue(link, "u"),
            token = FakeEmailSender.QueryValue(link, "t") + "x",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).ShouldBe("invalid_token");
        response.SessionCookieHeader().ShouldBeNull();
    }

    [Test]
    public async Task ConfirmEmail_UnknownUser_Returns400InvalidToken()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.PostAsJsonAsync("/api/identity/auth/confirm-email", new { userId = Guid.CreateVersion7(), token = "abc" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).ShouldBe("invalid_token");
    }

    [Test]
    public async Task ResendConfirmation_UnknownEmail_Returns202AndSendsNothing()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.PostAsJsonAsync("/api/identity/auth/resend-confirmation", new { email = "yok@ornek.com" });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Factory.Emails.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task ResendConfirmation_Unconfirmed_SendsNewLink()
    {
        using var client = Factory.CreateClientWithCookies();
        await RegisterAsync(client, "ali@ornek.com");

        using var response = await client.PostAsJsonAsync("/api/identity/auth/resend-confirmation", new { email = "ALI@ornek.com" });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Factory.Emails.To("ali@ornek.com").Count.ShouldBe(2);
    }
}
