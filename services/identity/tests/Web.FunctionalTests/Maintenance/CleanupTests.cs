using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Maintenance;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace IdentityService.Web.FunctionalTests.Maintenance;

public class CleanupTests : TestBase
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
    private static IdentityApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartFactoryWithFakeClock()
    {
        _factory = new IdentityApiFactory(TestEnvironment.ConnectionString, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    [OneTimeTearDown]
    public async Task StopFactory() => await _factory.DisposeAsync();

    [SetUp]
    public void ClearMail() => _factory.Emails.Clear();

    private static async Task<CleanupReport> RunCleanupAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RunAccountCleanupCommand());
    }

    private static async Task<UserSummary> CreateUserAsync(string email, bool confirmed)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IUserAccounts>()
            .CreateAsync(email, "Test Kullanıcı", TestUsers.Password, confirmed, CancellationToken.None);
    }

    private static async Task<UserSummary?> FindAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IUserAccounts>().FindByIdAsync(id, CancellationToken.None);
    }

    private static async Task<Membership> AddToNewCompanyAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var tenant = new Tenant { Name = "Firma", CreatedAt = Clock.GetUtcNow(), CreatedByUserId = userId };
        var roles = SystemRoles.CreateDefaults(tenant.Id);
        var membership = new Membership { TenantId = tenant.Id, UserId = userId, RoleId = roles[0].Id, JoinedAt = Clock.GetUtcNow() };
        db.Tenants.Add(tenant);
        db.TenantRoles.AddRange(roles);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync(CancellationToken.None);
        return membership;
    }

    [Test]
    public async Task Unconfirmed_Day8_Deleted_Day6_Kept()
    {
        var old = await CreateUserAsync("eski@ornek.com", confirmed: false);
        Clock.Advance(TimeSpan.FromDays(2));
        var recent = await CreateUserAsync("yeni@ornek.com", confirmed: false);
        Clock.Advance(TimeSpan.FromDays(6));

        var report = await RunCleanupAsync();

        report.UnconfirmedDeleted.ShouldBe(1);
        (await FindAsync(old.Id)).ShouldBeNull();
        (await FindAsync(recent.Id)).ShouldNotBeNull();
    }

    [Test]
    public async Task Companyless_Day23_ReminderOnce()
    {
        var user = await CreateUserAsync("firmasiz@ornek.com", confirmed: true);
        Clock.Advance(TimeSpan.FromDays(23));

        await RunCleanupAsync();
        var second = await RunCleanupAsync();

        second.RemindersSent.ShouldBe(0);
        var mail = _factory.Emails.To("firmasiz@ornek.com").ShouldHaveSingleItem();
        mail.Subject.ShouldBe("Modaryo: hesabınız yakında silinecek");
        (await FindAsync(user.Id)).ShouldNotBeNull();
    }

    [Test]
    public async Task Companyless_Day22_NoReminder()
    {
        await CreateUserAsync("firmasiz@ornek.com", confirmed: true);
        Clock.Advance(TimeSpan.FromDays(22));

        var report = await RunCleanupAsync();

        report.RemindersSent.ShouldBe(0);
        _factory.Emails.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task Companyless_Day30_DeletedWithSessions()
    {
        var user = await CreateUserAsync("firmasiz@ornek.com", confirmed: true);
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISessionStore>().CreateAsync(
                new NewSession(user.Id, user.Email, user.FullName, IdentityService.Application.Auth.SessionGrant.None, true, null, null), CancellationToken.None);
        }
        Clock.Advance(TimeSpan.FromDays(30));

        var report = await RunCleanupAsync();

        report.CompanylessDeleted.ShouldBe(1);
        (await FindAsync(user.Id)).ShouldBeNull();
        (await _factory.SessionsOfAsync(user.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task RemovedFromLastCompany_ClockStartsAtRemoval()
    {
        var user = await CreateUserAsync("ayrilan@ornek.com", confirmed: true);
        var membership = await AddToNewCompanyAsync(user.Id);
        Clock.Advance(TimeSpan.FromDays(40));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.Memberships.Remove(await db.Memberships.FindAsync(membership.Id) ?? throw new InvalidOperationException());
            await db.SaveChangesAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUserAccounts>().SetLastMembershipEndedAsync(user.Id, Clock.GetUtcNow());
        }
        Clock.Advance(TimeSpan.FromDays(1));

        var report = await RunCleanupAsync();

        report.CompanylessDeleted.ShouldBe(0);
        report.RemindersSent.ShouldBe(0);
        (await FindAsync(user.Id)).ShouldNotBeNull();
    }

    [Test]
    public async Task UserWithMembership_NeverTouched()
    {
        var user = await CreateUserAsync("uye@ornek.com", confirmed: true);
        await AddToNewCompanyAsync(user.Id);
        Clock.Advance(TimeSpan.FromDays(60));

        var report = await RunCleanupAsync();

        report.ShouldBe(new CleanupReport(0, 0, 0));
        (await FindAsync(user.Id)).ShouldNotBeNull();
    }
}
