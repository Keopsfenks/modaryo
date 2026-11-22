using IdentityService.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Modaryo.Sessions;
using StackExchange.Redis;

namespace IdentityService.Web.FunctionalTests.Sessions;

public class SessionStoreTests : TestBase
{
    private IServiceScope _scope = null!;
    private ISessionStore _store = null!;
    private IDatabase _db = null!;

    [SetUp]
    public void Resolve()
    {
        _scope = Factory.Services.CreateScope();
        _store = _scope.ServiceProvider.GetRequiredService<ISessionStore>();
        _db = _scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
    }

    [TearDown]
    public void Dispose() => _scope.Dispose();

    private static NewSession NewSession(Guid userId, Guid? tenantId = null, bool rememberMe = false) =>
        new(userId, "ali@ornek.com", "Ali Veli", tenantId, tenantId is null ? [] : ["*"], rememberMe, "203.0.113.5", "test-agent");

    [Test]
    public async Task Create_WritesRecordWithTtlAndIndex()
    {
        var userId = Guid.CreateVersion7();

        var created = await _store.CreateAsync(NewSession(userId), CancellationToken.None);

        created.SidHash.ShouldBe(SessionKeys.HashSid(created.Sid));
        var ttl = await _db.KeyTimeToLiveAsync(SessionKeys.SessionKey(created.SidHash));
        ttl!.Value.ShouldBeInRange(TimeSpan.FromHours(7.9), TimeSpan.FromHours(8));
        (await _db.SetContainsAsync(SessionKeys.UserIndexKey(userId), created.SidHash)).ShouldBeTrue();
        created.Record.IdleTimeoutS.ShouldBe(28800);
        (created.Record.AbsoluteExpiresAt - created.Record.CreatedAt).ShouldBe(24 * 3600);
        created.Record.V.ShouldBe(1);
        created.Record.Id.ShouldMatch("^[0-9a-f]{16}$");
    }

    [Test]
    public async Task Create_RememberMe_Uses14DayIdleAnd30DayAbsolute()
    {
        var created = await _store.CreateAsync(NewSession(Guid.CreateVersion7(), rememberMe: true), CancellationToken.None);

        created.Record.IdleTimeoutS.ShouldBe(1209600);
        (created.Record.AbsoluteExpiresAt - created.Record.CreatedAt).ShouldBe(30 * 24 * 3600);
    }

    [Test]
    public async Task Create_RecordIsReadableBySharedReader()
    {
        var created = await _store.CreateAsync(NewSession(Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);
        var reader = _scope.ServiceProvider.GetRequiredService<SessionReader>();

        var lookup = await reader.ReadAsync(created.Sid, CancellationToken.None);

        lookup.ShouldNotBeNull();
        lookup.Record.ShouldBeEquivalentTo(created.Record);
    }

    [Test]
    public async Task UpdateContent_DoesNotReviveDeletedSession()
    {
        var created = await _store.CreateAsync(NewSession(Guid.CreateVersion7()), CancellationToken.None);
        await _store.DeleteAsync(created.SidHash, CancellationToken.None);

        var updated = await _store.UpdateTenantAsync(created.SidHash, Guid.CreateVersion7(), ["*"], CancellationToken.None);

        updated.ShouldBeFalse();
        (await _db.KeyExistsAsync(SessionKeys.SessionKey(created.SidHash))).ShouldBeFalse();
    }

    [Test]
    public async Task UpdateContent_KeepsTtl()
    {
        var created = await _store.CreateAsync(NewSession(Guid.CreateVersion7()), CancellationToken.None);
        var key = SessionKeys.SessionKey(created.SidHash);
        await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(100));

        (await _store.UpdateTenantAsync(created.SidHash, Guid.CreateVersion7(), ["*"], CancellationToken.None)).ShouldBeTrue();

        (await _db.KeyTimeToLiveAsync(key))!.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(100));
    }

    [Test]
    public async Task UpdateTenant_ChangesTenantAndPermissions()
    {
        var created = await _store.CreateAsync(NewSession(Guid.CreateVersion7()), CancellationToken.None);
        var tenantId = Guid.CreateVersion7();

        await _store.UpdateTenantAsync(created.SidHash, tenantId, ["members.read"], CancellationToken.None);

        var stored = (await _store.ListForUserAsync(created.Record.UserId, CancellationToken.None)).Single();
        stored.Record.TenantId.ShouldBe(tenantId);
        stored.Record.Permissions.ShouldBe(["members.read"]);
    }

    [Test]
    public async Task UpdateTenantForUser_OnlyTouchesMatchingTenant()
    {
        var userId = Guid.CreateVersion7();
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var inA = await _store.CreateAsync(NewSession(userId, tenantA), CancellationToken.None);
        var inB = await _store.CreateAsync(NewSession(userId, tenantB), CancellationToken.None);

        await _store.UpdateTenantForUserAsync(userId, tenantA, null, [], CancellationToken.None);

        var sessions = await _store.ListForUserAsync(userId, CancellationToken.None);
        sessions.Single(s => s.SidHash == inA.SidHash).Record.TenantId.ShouldBeNull();
        sessions.Single(s => s.SidHash == inA.SidHash).Record.Permissions.ShouldBeEmpty();
        sessions.Single(s => s.SidHash == inB.SidHash).Record.TenantId.ShouldBe(tenantB);
        sessions.Single(s => s.SidHash == inB.SidHash).Record.Permissions.ShouldBe(["*"]);
    }

    [Test]
    public async Task DeleteAllForUser_ExceptCurrent()
    {
        var userId = Guid.CreateVersion7();
        var current = await _store.CreateAsync(NewSession(userId), CancellationToken.None);
        await _store.CreateAsync(NewSession(userId), CancellationToken.None);
        await _store.CreateAsync(NewSession(userId), CancellationToken.None);

        await _store.DeleteAllForUserAsync(userId, current.SidHash, CancellationToken.None);

        (await _store.ListForUserAsync(userId, CancellationToken.None)).Select(s => s.SidHash).ShouldBe([current.SidHash]);
    }

    [Test]
    public async Task DeleteAllForUser_WithoutException_DeletesEverything()
    {
        var userId = Guid.CreateVersion7();
        var first = await _store.CreateAsync(NewSession(userId), CancellationToken.None);
        await _store.CreateAsync(NewSession(userId), CancellationToken.None);

        await _store.DeleteAllForUserAsync(userId, null, CancellationToken.None);

        (await _store.ListForUserAsync(userId, CancellationToken.None)).ShouldBeEmpty();
        (await _db.KeyExistsAsync(SessionKeys.SessionKey(first.SidHash))).ShouldBeFalse();
    }

    [Test]
    public async Task DeleteByPublicId_OnlyForOwner()
    {
        var owner = Guid.CreateVersion7();
        var created = await _store.CreateAsync(NewSession(owner), CancellationToken.None);

        (await _store.DeleteByPublicIdAsync(Guid.CreateVersion7(), created.Record.Id, CancellationToken.None)).ShouldBeFalse();
        (await _store.DeleteByPublicIdAsync(owner, created.Record.Id, CancellationToken.None)).ShouldBeTrue();
        (await _store.ListForUserAsync(owner, CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task ListForUser_PrunesStaleIndexMembers()
    {
        var userId = Guid.CreateVersion7();
        var live = await _store.CreateAsync(NewSession(userId), CancellationToken.None);
        var stale = await _store.CreateAsync(NewSession(userId), CancellationToken.None);
        await _db.KeyDeleteAsync(SessionKeys.SessionKey(stale.SidHash)); // süresi dolmuş gibi

        var sessions = await _store.ListForUserAsync(userId, CancellationToken.None);

        sessions.Select(s => s.SidHash).ShouldBe([live.SidHash]);
        (await _db.SetContainsAsync(SessionKeys.UserIndexKey(userId), stale.SidHash)).ShouldBeFalse();
    }
}
