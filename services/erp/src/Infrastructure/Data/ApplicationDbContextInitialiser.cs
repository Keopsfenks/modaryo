using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Data;

public static class InitialiserExtensions {
    /// <summary>Yalnızca geliştirme ortamında çağrılır (Program.cs).</summary>
    public static async Task InitialiseDatabaseAsync(this WebApplication app) {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>().InitialiseAsync();
    }
}

public class ApplicationDbContextInitialiser(
    ILogger<ApplicationDbContextInitialiser> logger,
    ApplicationDbContext                     context,
    StoredAllocationsBackfill                storedAllocations
) {
    /// <summary>Bekleyen migration'ları uygular (veritabanını asla silmez), ardından tek seferlik veri adımlarını çalıştırır.</summary>
    public async Task InitialiseAsync() {
        try {
            await context.Database.MigrateAsync();
            await storedAllocations.RunAsync(CancellationToken.None);
        }
        catch (Exception ex) {
            logger.LogError(ex, "An error occurred while migrating the database.");
            throw;
        }
    }
}