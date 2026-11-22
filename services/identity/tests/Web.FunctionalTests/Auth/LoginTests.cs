using System.Text.Json;

namespace IdentityService.Web.FunctionalTests.Auth;

public class LoginTests : TestBase
{
    [Test]
    public async Task Login_Valid_Returns200AndCookie()
    {
        await Factory.CreateConfirmedAsync("ali@ornek.com", "Ali Veli");
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.LoginAsync("ali@ornek.com");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.SessionCookieHeader().ShouldNotBeNull();
        var body = await response.JsonAsync();
        body.GetProperty("user").GetProperty("fullName").GetString().ShouldBe("Ali Veli");
        body.GetProperty("companies").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Login_EmailIsCaseAndWhitespaceInsensitive()
    {
        await Factory.CreateConfirmedAsync("ali@ornek.com");
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.LoginAsync("  ALI@Ornek.com ");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Login_RememberMe_SetsPersistentCookieAnd14DayIdle()
    {
        var user = await Factory.CreateConfirmedAsync("ali@ornek.com");
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.LoginAsync("ali@ornek.com", rememberMe: true);

        response.SessionCookieHeader()!.ToLowerInvariant().ShouldContain("expires=");
        var session = (await Factory.SessionsOfAsync(user.Id)).Single();
        session.Record.IdleTimeoutS.ShouldBe(1209600);
        session.Record.RememberMe.ShouldBeTrue();
    }

    [Test]
    public async Task Login_WrongPassword_And_UnknownEmail_ReturnSameBody()
    {
        await Factory.CreateConfirmedAsync("ali@ornek.com");
        using var client = Factory.CreateClientWithCookies();

        using var wrongPassword = await client.LoginAsync("ali@ornek.com", "yanlis-sifre-000");
        using var unknownEmail = await client.LoginAsync("yok@ornek.com");

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var a = await wrongPassword.JsonAsync();
        var b = await unknownEmail.JsonAsync();
        a.GetProperty("code").GetString().ShouldBe("invalid_credentials");
        a.GetProperty("title").GetString().ShouldBe(b.GetProperty("title").GetString());
        b.GetProperty("code").GetString().ShouldBe("invalid_credentials");
    }

    [Test]
    public async Task Login_Unconfirmed_Returns403Code()
    {
        await Factory.CreateConfirmedAsync("ali@ornek.com", confirmed: false);
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.LoginAsync("ali@ornek.com");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).ShouldBe("email_not_confirmed");
        response.SessionCookieHeader().ShouldBeNull();
    }

    [Test]
    public async Task Login_FiveFailures_LocksFor15Minutes()
    {
        await Factory.CreateConfirmedAsync("ali@ornek.com");
        using var client = Factory.CreateClientWithCookies();
        for (var i = 0; i < 5; i++)
        {
            using var failed = await client.LoginAsync("ali@ornek.com", "yanlis-sifre-000");
        }

        using var response = await client.LoginAsync("ali@ornek.com");

        response.StatusCode.ShouldBe((HttpStatusCode)423);
        (await response.ErrorCodeAsync()).ShouldBe("account_locked");
        (await response.JsonAsync()).GetProperty("title").GetString()!.ShouldContain("15 dakika");
    }

    [Test]
    public async Task Login_RateLimit_11thRequestInMinute_Returns429()
    {
        await using var limited = new IdentityApiFactory(TestEnvironment.ConnectionString, new Dictionary<string, string?> { ["RateLimits:Enabled"] = "true" });
        using var client = limited.CreateClientWithCookies();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 11; i++)
        {
            // farklı e-postalar: e-posta başına sınır değil, IP sınırı test ediliyor
            using var response = await client.LoginAsync($"kisi{i}@ornek.com");
            statuses.Add(response.StatusCode);
        }

        statuses.Take(10).ShouldAllBe(s => s == HttpStatusCode.Unauthorized);
        statuses[10].ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task Login_Again_ReplacesPreviousSession()
    {
        var user = await Factory.CreateConfirmedAsync("ali@ornek.com");
        using var client = Factory.CreateClientWithCookies();
        (await client.LoginAsync("ali@ornek.com")).EnsureSuccessStatusCode();

        (await client.LoginAsync("ali@ornek.com")).EnsureSuccessStatusCode();

        (await Factory.SessionsOfAsync(user.Id)).Count.ShouldBe(1);
    }

    [Test]
    public async Task Session_WithoutCookie_Returns401()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.GetAsync("/api/identity/auth/session");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Session_WithoutCompany_ReturnsNullActiveCompany()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");

        using var response = await client.GetAsync("/api/identity/auth/session");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("user").GetProperty("email").GetString().ShouldBe("ali@ornek.com");
        body.GetProperty("activeCompany").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("permissions").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Logout_DeletesSessionKey()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");

        using var logout = await client.PostAsync("/api/identity/auth/logout", null);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/identity/auth/session")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Logout_WithoutSession_IsIdempotent()
    {
        using var client = Factory.CreateClientWithCookies();

        using var logout = await client.PostAsync("/api/identity/auth/logout", null);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task LogoutAll_DeletesEveryDevice()
    {
        using var phone = await Factory.SignedInClientAsync("ali@ornek.com");
        using var laptop = Factory.CreateClientWithCookies();
        (await laptop.LoginAsync("ali@ornek.com")).EnsureSuccessStatusCode();

        using var response = await laptop.PostAsync("/api/identity/auth/logout-all", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await phone.GetAsync("/api/identity/auth/session")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await laptop.GetAsync("/api/identity/auth/session")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SwitchTenant_NotMember_Returns403()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");

        using var response = await client.PostAsJsonAsync("/api/identity/auth/session/tenant", new { tenantId = Guid.CreateVersion7() });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
