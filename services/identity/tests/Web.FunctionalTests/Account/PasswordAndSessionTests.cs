namespace IdentityService.Web.FunctionalTests.Account;

public class PasswordAndSessionTests : TestBase
{
    private const string NewPassword = "yepyeni-sifre-789";

    private static async Task<(string Email, string Token)> RequestResetAsync(HttpClient client, IdentityApiFactory factory, string email)
    {
        (await client.PostAsJsonAsync("/api/identity/auth/forgot-password", new { email })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var link = FakeEmailSender.ExtractLink(factory.Emails.LastTo(email));
        return (FakeEmailSender.QueryValue(link, "e"), FakeEmailSender.QueryValue(link, "t"));
    }

    [Test]
    public async Task ForgotPassword_UnknownEmail_Returns202NoMail()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.PostAsJsonAsync("/api/identity/auth/forgot-password", new { email = "yok@ornek.com" });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Factory.Emails.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task ResetPassword_ValidToken_ChangesPasswordAndKillsAllSessions()
    {
        using var phone = await Factory.SignedInClientAsync("ali@ornek.com");
        using var anonymous = Factory.CreateClientWithCookies();
        var (email, token) = await RequestResetAsync(anonymous, Factory, "ali@ornek.com");

        using var reset = await anonymous.PostAsJsonAsync("/api/identity/auth/reset-password", new { email, token, newPassword = NewPassword });

        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await phone.GetAsync("/api/identity/auth/session")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.LoginAsync("ali@ornek.com", TestUsers.Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.LoginAsync("ali@ornek.com", NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task ResetPassword_TokenUsedTwice_Returns400()
    {
        await Factory.CreateConfirmedAsync("ali@ornek.com");
        using var client = Factory.CreateClientWithCookies();
        var (email, token) = await RequestResetAsync(client, Factory, "ali@ornek.com");
        (await client.PostAsJsonAsync("/api/identity/auth/reset-password", new { email, token, newPassword = NewPassword })).EnsureSuccessStatusCode();

        using var again = await client.PostAsJsonAsync("/api/identity/auth/reset-password", new { email, token, newPassword = "baska-sifre-0000" });

        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await again.ErrorCodeAsync()).ShouldBe("invalid_token");
    }

    [Test]
    public async Task ResetPassword_ExpiredToken_Returns400()
    {
        await using var shortLived = new IdentityApiFactory(TestEnvironment.ConnectionString,
            new Dictionary<string, string?> { ["Tokens:PasswordResetLifetimeSeconds"] = "1" });
        await shortLived.CreateConfirmedAsync("ali@ornek.com");
        using var client = shortLived.CreateClientWithCookies();
        var (email, token) = await RequestResetAsync(client, shortLived, "ali@ornek.com");

        await Task.Delay(TimeSpan.FromSeconds(2.5));
        using var response = await client.PostAsJsonAsync("/api/identity/auth/reset-password", new { email, token, newPassword = NewPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).ShouldBe("invalid_token");
    }

    [Test]
    public void ResetPassword_DefaultLifetimeIsOneHour()
    {
        var options = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions>>(Factory.Services);

        options.Value.TokenLifespan.ShouldBe(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task ResetPassword_ShortNewPassword_Returns400()
    {
        await Factory.CreateConfirmedAsync("ali@ornek.com");
        using var client = Factory.CreateClientWithCookies();
        var (email, token) = await RequestResetAsync(client, Factory, "ali@ornek.com");

        using var response = await client.PostAsJsonAsync("/api/identity/auth/reset-password", new { email, token, newPassword = "kisa" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.LoginAsync("ali@ornek.com")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task ChangePassword_KeepsCurrentSessionKillsOthers()
    {
        using var laptop = await Factory.SignedInClientAsync("ali@ornek.com");
        using var phone = Factory.CreateClientWithCookies();
        (await phone.LoginAsync("ali@ornek.com")).EnsureSuccessStatusCode();

        using var response = await laptop.PostAsJsonAsync("/api/identity/account/change-password",
            new { currentPassword = TestUsers.Password, newPassword = NewPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await laptop.GetAsync("/api/identity/auth/session")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await phone.GetAsync("/api/identity/auth/session")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ChangePassword_WrongCurrent_Returns400()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");

        using var response = await client.PostAsJsonAsync("/api/identity/account/change-password",
            new { currentPassword = "yanlis-sifre-000", newPassword = NewPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UpdateProfile_ChangesFullName()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com", "Ali Veli");

        using var response = await client.PatchAsJsonAsync("/api/identity/account/profile", new { fullName = "  Ali Can Veli " });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var session = await (await client.GetAsync("/api/identity/auth/session")).JsonAsync();
        session.GetProperty("user").GetProperty("fullName").GetString().ShouldBe("Ali Can Veli");
    }

    [Test]
    public async Task Sessions_ListShowsCurrentFlagAndForwardedIp()
    {
        await Factory.CreateConfirmedAsync("ali@ornek.com");
        using var laptop = Factory.CreateClientWithCookies();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/identity/auth/login")
        {
            Content = JsonContent.Create(new { email = "ali@ornek.com", password = TestUsers.Password, rememberMe = false }),
        };
        login.Headers.Add("X-Forwarded-For", "203.0.113.5");
        login.Headers.Add("User-Agent", "Laptop-Tarayici/1.0");
        (await laptop.SendAsync(login)).EnsureSuccessStatusCode();
        using var phone = Factory.CreateClientWithCookies();
        (await phone.LoginAsync("ali@ornek.com")).EnsureSuccessStatusCode();

        var sessions = await (await laptop.GetAsync("/api/identity/account/sessions")).JsonAsync();

        sessions.GetArrayLength().ShouldBe(2);
        var current = sessions.EnumerateArray().Single(s => s.GetProperty("isCurrent").GetBoolean());
        current.GetProperty("ip").GetString().ShouldBe("203.0.113.5");
        current.GetProperty("userAgent").GetString().ShouldBe("Laptop-Tarayici/1.0");
        current.GetProperty("id").GetString()!.ShouldMatch("^[0-9a-f]{16}$");
    }

    [Test]
    public async Task RevokeSession_Own_LogsThatDeviceOut()
    {
        using var laptop = await Factory.SignedInClientAsync("ali@ornek.com");
        using var phone = Factory.CreateClientWithCookies();
        (await phone.LoginAsync("ali@ornek.com")).EnsureSuccessStatusCode();
        var sessions = await (await laptop.GetAsync("/api/identity/account/sessions")).JsonAsync();
        var phoneId = sessions.EnumerateArray().Single(s => !s.GetProperty("isCurrent").GetBoolean()).GetProperty("id").GetString();

        using var response = await laptop.DeleteAsync($"/api/identity/account/sessions/{phoneId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await phone.GetAsync("/api/identity/auth/session")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await laptop.GetAsync("/api/identity/auth/session")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task RevokeSession_OtherUsersSessionId_Returns404()
    {
        using var ali = await Factory.SignedInClientAsync("ali@ornek.com");
        using var veli = await Factory.SignedInClientAsync("veli@ornek.com");
        var veliSessions = await (await veli.GetAsync("/api/identity/account/sessions")).JsonAsync();
        var veliSessionId = veliSessions[0].GetProperty("id").GetString();

        using var response = await ali.DeleteAsync($"/api/identity/account/sessions/{veliSessionId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await veli.GetAsync("/api/identity/auth/session")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
