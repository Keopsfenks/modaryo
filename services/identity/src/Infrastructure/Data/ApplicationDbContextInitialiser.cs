using IdentityService.Application.Common.Interfaces;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdentityService.Infrastructure.Data;

public static class InitialiserExtensions
{
    /// <summary>Yalnızca geliştirme ortamında çağrılır (Program.cs).</summary>
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

        await initialiser.InitialiseAsync();
        await initialiser.SeedAsync();
    }
}

public class ApplicationDbContextInitialiser(
    ILogger<ApplicationDbContextInitialiser> logger,
    ApplicationDbContext context,
    IUserAccounts users,
    IConfiguration configuration,
    TimeProvider time)
{
    public const string DemoEmail = "demo@modaryo.local";

    /// <summary>Bekleyen migration'ları uygular (veritabanını asla silmez).</summary>
    public async Task InitialiseAsync()
    {
        try
        {
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while migrating the database.");
            throw;
        }
    }

    /// <summary>
    /// Geliştirme verisi: "Seed:DemoPassword" ayarlıysa demo@modaryo.local kullanıcısı ve sahibi olduğu "Demo Firma".
    /// Kullanıcı zaten varsa hiçbir şey yapmaz.
    /// </summary>
    public async Task SeedAsync()
    {
        try
        {
            var password = configuration["Seed:DemoPassword"];
            if (string.IsNullOrWhiteSpace(password) || await users.FindByEmailAsync(DemoEmail, CancellationToken.None) is not null)
            {
                return;
            }

            var user = await users.CreateAsync(DemoEmail, "Demo Kullanıcı", password, emailConfirmed: true, CancellationToken.None);
            var now = time.GetUtcNow();
            var tenant = new Tenant { Name = "Demo Firma", CreatedAt = now, CreatedByUserId = user.Id };
            var roles = SystemRoles.CreateDefaults(tenant.Id);

            context.Tenants.Add(tenant);
            context.TenantRoles.AddRange(roles);
            context.Memberships.Add(new Membership
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                RoleId = roles.Single(r => r.SystemKey == SystemRoles.Owner).Id,
                JoinedAt = now,
            });
            await context.SaveChangesAsync();
            await users.SetLastActiveTenantAsync(user.Id, tenant.Id);

            logger.LogInformation("Geliştirme verisi oluşturuldu: {Email} / Demo Firma", DemoEmail);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }
}
