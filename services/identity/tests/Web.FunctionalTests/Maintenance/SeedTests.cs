using IdentityService.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Maintenance;

public class SeedTests : TestBase
{
    private static async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>().SeedAsync();
    }

    [Test]
    public async Task DevSeed_CreatesDemoUserWithOwnCompany_AndIsIdempotent()
    {
        await SeedAsync();
        await SeedAsync();

        var password = Factory.Services.GetRequiredService<IConfiguration>()["Seed:DemoPassword"];
        password.ShouldNotBeNullOrWhiteSpace();
        using var client = Factory.CreateClientWithCookies();
        using var login = await client.LoginAsync("demo@modaryo.local", password);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = await login.JsonAsync();
        session.GetProperty("user").GetProperty("fullName").GetString().ShouldBe("Demo Kullanıcı");
        session.GetProperty("companies").GetArrayLength().ShouldBe(1);
        session.GetProperty("activeCompany").GetProperty("name").GetString().ShouldBe("Demo Firma");
        session.GetProperty("activeCompany").GetProperty("roleName").GetString().ShouldBe("Sahip");
    }
}
