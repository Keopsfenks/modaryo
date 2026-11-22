using IdentityService.Application.Common.Interfaces;
using IdentityService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Infrastructure;

public static class TestCompanies
{
    /// <summary>Giriş yapmış istemciyle firma oluşturur ve kimliğini döndürür.</summary>
    public static async Task<Guid> CreateCompanyAsync(this HttpClient client, string name = "Test A.Ş.")
    {
        using var response = await client.PostAsJsonAsync("/api/identity/companies", new { name });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> RoleIdAsync(this IdentityApiFactory factory, Guid tenantId, string systemKey)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        return await db.TenantRoles.Where(r => r.TenantId == tenantId && r.SystemKey == systemKey).Select(r => r.Id).SingleAsync();
    }

    /// <summary>Üyeliği doğrudan veritabanına ekler (davet akışını atlar).</summary>
    public static async Task AddMemberAsync(this IdentityApiFactory factory, Guid tenantId, Guid userId, string systemKey)
    {
        var roleId = await factory.RoleIdAsync(tenantId, systemKey);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        db.Memberships.Add(new Membership { TenantId = tenantId, UserId = userId, RoleId = roleId, JoinedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Kullanıcı oluşturur, firmaya ekler ve giriş yaptırır (aktif firma = bu firma).</summary>
    public static async Task<(HttpClient Client, Guid UserId)> SignedInMemberAsync(this IdentityApiFactory factory, Guid tenantId, string email, string systemKey)
    {
        var user = await factory.CreateConfirmedAsync(email);
        await factory.AddMemberAsync(tenantId, user.Id, systemKey);
        var client = factory.CreateClientWithCookies();
        (await client.LoginAsync(email)).EnsureSuccessStatusCode();
        return (client, user.Id);
    }

    public static async Task<System.Text.Json.JsonElement> SessionAsync(this HttpClient client)
    {
        using var response = await client.GetAsync("/api/identity/auth/session");
        response.EnsureSuccessStatusCode();
        return await response.JsonAsync();
    }
}
