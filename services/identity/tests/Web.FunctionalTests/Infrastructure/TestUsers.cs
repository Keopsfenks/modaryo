using IdentityService.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Infrastructure;

public static class TestUsers
{
    public const string Password = "guvenli-sifre-123";

    /// <summary>E-postası doğrulanmış bir kullanıcıyı doğrudan oluşturur (HTTP akışını atlar).</summary>
    public static async Task<UserSummary> CreateConfirmedAsync(this IdentityApiFactory factory, string email, string fullName = "Test Kullanıcı", bool confirmed = true)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserAccounts>();
        return await users.CreateAsync(email, fullName, Password, confirmed, CancellationToken.None);
    }

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password = Password, bool rememberMe = false) =>
        client.PostAsJsonAsync("/api/identity/auth/login", new { email, password, rememberMe });

    /// <summary>Kullanıcıyı oluşturur ve giriş yapmış bir istemci döndürür.</summary>
    public static async Task<HttpClient> SignedInClientAsync(this IdentityApiFactory factory, string email, string fullName = "Test Kullanıcı")
    {
        await factory.CreateConfirmedAsync(email, fullName);
        var client = factory.CreateClientWithCookies();
        using var response = await client.LoginAsync(email);
        response.EnsureSuccessStatusCode();
        return client;
    }

    public static async Task<IReadOnlyList<StoredSession>> SessionsOfAsync(this IdentityApiFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISessionStore>().ListForUserAsync(userId, CancellationToken.None);
    }
}
