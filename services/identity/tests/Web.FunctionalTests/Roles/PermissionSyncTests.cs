using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Auth;
using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using IdentityService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Roles;

public class PermissionSyncTests : TestBase
{
    private static async Task<Modaryo.Sessions.SessionRecord> RecordAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        return (await store.ListForUserAsync(userId, CancellationToken.None)).Single().Record;
    }

    private static async Task<int> SyncAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SystemRolePermissionSync>().RunAsync(CancellationToken.None);
    }

    private static async Task<(Guid TenantId, Guid CustomRoleId)> SeedLegacyTenantAsync(int version = 1, string[]? adminPermissions = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = new Tenant { Name = "Eski Firma", CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = Guid.CreateVersion7(), AppliedPermissionSetVersion = version };
        db.Tenants.Add(tenant);
        db.TenantRoles.Add(LegacyRole(tenant.Id, "Sahip", "owner", ["*"]));
        db.TenantRoles.Add(LegacyRole(tenant.Id, "Yönetici", "admin",
            adminPermissions ?? AdminV1));
        db.TenantRoles.Add(LegacyRole(tenant.Id, "Kullanıcı", "member", ["members.read", "cari.read"]));
        var custom = LegacyRole(tenant.Id, "Muhasebe", null, ["members.read"]);
        db.TenantRoles.Add(custom);
        await db.SaveChangesAsync();
        return (tenant.Id, custom.Id);
    }

    private static readonly string[] AdminV1 =
        ["company.update", "members.read", "members.invite", "members.manage", "cari.read", "cari.manage", "cari.movements"];

    private static readonly string[] V2Codes =
        ["ayarlar.moduller", "ayarlar.subeler", "denetim.read", "roller.manage", "roller.read"];

    private static readonly string[] V3Codes = ["ayarlar.finans", "ayarlar.firma", "ayarlar.genel"];

    private static readonly string[] V4Codes = ["ayarlar.katalog", "katalog.manage", "katalog.read"];

    private static readonly string[] V5Codes = ["ayarlar.fiyat", "fiyat.alis", "fiyat.manage", "fiyat.read"];

    private static readonly string[] V6Codes = ["ayarlar.stok", "maliyet.read", "stok.manage", "stok.read", "stok.sayim"];

    private static readonly string[] V7Codes =
        ["ayarlar.belgeler", "ayarlar.ebelge", "fatura.cancel", "fatura.issue", "fatura.manage", "fatura.read", "kur.manage"];

    private static readonly string[] V8Codes =
        ["ceksenet.manage", "finans.manage", "finans.read", "masraf.manage", "muhasebe.export", "muhasebe.manage", "odeme.manage", "tahsilat.manage", "virman.manage"];

    private static readonly string[] NewCodes = V2Codes.Concat(V3Codes).Concat(V4Codes).Concat(V5Codes).Concat(V6Codes).Concat(V7Codes).Concat(V8Codes).ToArray();

    private static TenantRole LegacyRole(Guid tenantId, string name, string? systemKey, string[] permissions)
    {
        var role = new TenantRole { TenantId = tenantId, Name = name, SystemKey = systemKey, IsSystem = systemKey is not null };
        role.Permissions.AddRange(permissions.Select(p => new TenantRolePermission { RoleId = role.Id, Permission = p }));
        return role;
    }

    private static async Task<string[]> PermissionsOfAsync(Guid tenantId, string? systemKey, string? name = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = await db.TenantRoles.Include(r => r.Permissions)
            .SingleAsync(r => r.TenantId == tenantId && r.SystemKey == systemKey && (name == null || r.Name == name));
        return role.Permissions.Select(p => p.Permission).Order().ToArray();
    }

    [Test]
    public async Task Sync_AppliesVersion2ToExistingTenant()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(1);

        var added = await SyncAsync();

        added.ShouldBeGreaterThanOrEqualTo(5);
        var admin = await PermissionsOfAsync(tenantId, "admin");
        admin.ShouldBe(AdminV1.Concat(NewCodes).Order().ToArray());
        (await PermissionsOfAsync(tenantId, "member")).ShouldBe(["cari.read", "fatura.read", "finans.read", "fiyat.read", "katalog.read", "members.read", "stok.read"]);
        (await PermissionsOfAsync(tenantId, "owner")).ShouldBe(["*"]);
    }

    [Test]
    public async Task Sync_AppliesVersion3SettingsPermissionsToAdmin()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(2, AdminV1.Concat(V2Codes).ToArray());

        var added = await SyncAsync();

        added.ShouldBeGreaterThanOrEqualTo(3);
        (await PermissionsOfAsync(tenantId, "admin")).ShouldBe(AdminV1.Concat(NewCodes).Order().ToArray());
        (await PermissionsOfAsync(tenantId, "member")).ShouldBe(["cari.read", "fatura.read", "finans.read", "fiyat.read", "katalog.read", "members.read", "stok.read"]);
        (await PermissionsOfAsync(tenantId, "owner")).ShouldBe(["*"]);
    }

    [Test]
    public async Task Sync_AppliesVersion4CatalogPermissions()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(3, AdminV1.Concat(V2Codes).Concat(V3Codes).ToArray());

        var added = await SyncAsync();

        added.ShouldBeGreaterThanOrEqualTo(4);
        (await PermissionsOfAsync(tenantId, "admin")).ShouldBe(AdminV1.Concat(NewCodes).Order().ToArray());
        (await PermissionsOfAsync(tenantId, "member")).ShouldBe(["cari.read", "fatura.read", "finans.read", "fiyat.read", "katalog.read", "members.read", "stok.read"]);
        (await PermissionsOfAsync(tenantId, "owner")).ShouldBe(["*"]);
        PermissionCatalog.All.Where(p => p.Group == "Katalog").Select(p => p.Code).Order().ToArray()
            .ShouldBe(["ayarlar.katalog", "katalog.manage", "katalog.read"], ignoreOrder: true);
    }

    [Test]
    public async Task Sync_AppliesVersion5PricingPermissions()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(4, AdminV1.Concat(V2Codes).Concat(V3Codes).Concat(V4Codes).ToArray());

        var added = await SyncAsync();

        added.ShouldBeGreaterThanOrEqualTo(5);
        (await PermissionsOfAsync(tenantId, "admin")).ShouldBe(AdminV1.Concat(NewCodes).Order().ToArray());
        (await PermissionsOfAsync(tenantId, "member")).ShouldBe(["cari.read", "fatura.read", "finans.read", "fiyat.read", "members.read", "stok.read"]);
        (await PermissionsOfAsync(tenantId, "owner")).ShouldBe(["*"]);
        PermissionCatalog.All.Where(p => p.Group == "Fiyatlandırma").Select(p => p.Code).ToArray()
            .ShouldBe(["fiyat.read", "fiyat.manage", "fiyat.alis", "ayarlar.fiyat"], ignoreOrder: true);
    }

    [Test]
    public async Task Sync_AppliesVersion6StockPermissions()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(5, AdminV1.Concat(V2Codes).Concat(V3Codes).Concat(V4Codes).Concat(V5Codes).ToArray());

        var added = await SyncAsync();

        added.ShouldBeGreaterThanOrEqualTo(6);
        (await PermissionsOfAsync(tenantId, "admin")).ShouldBe(AdminV1.Concat(NewCodes).Order().ToArray());
        (await PermissionsOfAsync(tenantId, "member")).ShouldBe(["cari.read", "fatura.read", "finans.read", "members.read", "stok.read"]);
        (await PermissionsOfAsync(tenantId, "owner")).ShouldBe(["*"]);
        PermissionCatalog.All.Where(p => p.Group == "Stok").Select(p => p.Code).ToArray()
            .ShouldBe(["stok.read", "stok.manage", "stok.sayim", "maliyet.read", "ayarlar.stok"], ignoreOrder: true);
    }

    [Test]
    public async Task Sync_AppliesVersion7InvoicePermissions()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(6, AdminV1.Concat(V2Codes).Concat(V3Codes).Concat(V4Codes).Concat(V5Codes).Concat(V6Codes).ToArray());

        var added = await SyncAsync();

        added.ShouldBeGreaterThanOrEqualTo(7);
        (await PermissionsOfAsync(tenantId, "admin")).ShouldBe(AdminV1.Concat(NewCodes).Order().ToArray());
        (await PermissionsOfAsync(tenantId, "member")).ShouldBe(["cari.read", "fatura.read", "finans.read", "members.read"]);
        (await PermissionsOfAsync(tenantId, "owner")).ShouldBe(["*"]);
        PermissionCatalog.All.Where(p => p.Group == "Fatura").Select(p => p.Code).ToArray()
            .ShouldBe(V7Codes, ignoreOrder: true);
    }

    [Test]
    public async Task Sync_AppliesVersion8FinancePermissions()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(7, AdminV1.Concat(V2Codes).Concat(V3Codes).Concat(V4Codes).Concat(V5Codes).Concat(V6Codes).Concat(V7Codes).ToArray());

        var added = await SyncAsync();

        added.ShouldBeGreaterThanOrEqualTo(10);
        SystemRoles.LatestPermissionSetVersion.ShouldBe(8);
        (await PermissionsOfAsync(tenantId, "admin")).ShouldBe(AdminV1.Concat(NewCodes).Order().ToArray());
        (await PermissionsOfAsync(tenantId, "member")).ShouldBe(["cari.read", "finans.read", "members.read"]);
        (await PermissionsOfAsync(tenantId, "owner")).ShouldBe(["*"]);
        PermissionCatalog.All.Where(p => p.Group == "Finans").Select(p => p.Code).ToArray()
            .ShouldBe(V8Codes, ignoreOrder: true);
    }

    [Test]
    public async Task Sync_DoesNotReaddPermissionRemovedByUser()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(SystemRoles.LatestPermissionSetVersion);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var role = await db.TenantRoles.Include(r => r.Permissions)
                .SingleAsync(r => r.TenantId == tenantId && r.SystemKey == "admin");
            role.Permissions.RemoveAll(p => p.Permission == "cari.movements");
            await db.SaveChangesAsync();
        }

        await SyncAsync();

        (await PermissionsOfAsync(tenantId, "admin")).ShouldNotContain("cari.movements");
    }

    [Test]
    public async Task Sync_ConcurrentRuns_DoNotFail()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(1);

        var results = await Task.WhenAll(SyncAsync(), SyncAsync());

        results.Min().ShouldBe(0);
        (await PermissionsOfAsync(tenantId, "admin")).Length.ShouldBe(AdminV1.Length + NewCodes.Length);
    }

    [Test]
    public async Task Sync_IsIdempotent()
    {
        await SeedLegacyTenantAsync(1);

        (await SyncAsync()).ShouldBeGreaterThan(0);
        (await SyncAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Sync_DoesNotTouchCustomRoles()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(1);

        await SyncAsync();

        (await PermissionsOfAsync(tenantId, null, "Muhasebe")).ShouldBe(["members.read"]);
    }

    [Test]
    public async Task Sync_RefreshesLiveSessionsOfTouchedTenants()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var admin = await Factory.CreateConfirmedAsync("yonetici@ornek.com");
        await Factory.AddMemberAsync(tenantId, admin.Id, "admin");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Tenants.SingleAsync(t => t.Id == tenantId)).AppliedPermissionSetVersion = 1;
            var role = await db.TenantRoles.Include(r => r.Permissions).SingleAsync(r => r.TenantId == tenantId && r.SystemKey == "admin");
            role.Permissions.RemoveAll(p => NewCodes.Contains(p.Permission));
            await db.SaveChangesAsync();
        }

        using var client = Factory.CreateClientWithCookies();
        (await client.LoginAsync("yonetici@ornek.com")).EnsureSuccessStatusCode();
        (await RecordAsync(admin.Id)).Permissions.ShouldNotContain("roller.read");

        await SyncAsync();

        (await RecordAsync(admin.Id)).Permissions.ShouldContain("roller.read");
    }

    [Test]
    public async Task Sync_SessionStoreDown_StillCommitsAndDoesNotThrow()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Tenants.SingleAsync(t => t.Id == tenantId)).AppliedPermissionSetVersion = 1;
            var role = await db.TenantRoles.Include(r => r.Permissions).SingleAsync(r => r.TenantId == tenantId && r.SystemKey == "admin");
            role.Permissions.RemoveAll(p => NewCodes.Contains(p.Permission));
            await db.SaveChangesAsync();
        }

        var logger = new CapturingLogger();
        int added;
        using (var scope = Factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var changes = new MembershipChanges(new ThrowingSessionStore(), sp.GetRequiredService<ActiveTenantResolver>(),
                sp.GetRequiredService<IUserAccounts>(), sp.GetRequiredService<IApplicationDbContext>(), TimeProvider.System);
            var sync = new SystemRolePermissionSync(sp.GetRequiredService<ApplicationDbContext>(), changes, logger);

            added = await sync.RunAsync(CancellationToken.None);
        }

        added.ShouldBeGreaterThan(0);
        (await PermissionsOfAsync(tenantId, "admin")).ShouldContain("roller.read");
        logger.Warnings.ShouldBe(1);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<SystemRolePermissionSync>
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning) Warnings++;
        }
    }

    private sealed class ThrowingSessionStore : ISessionStore
    {
        private static Exception Down() => new InvalidOperationException("Valkey kapalı.");

        public Task<CreatedSession> CreateAsync(NewSession session, CancellationToken ct) => throw Down();

        public Task<bool> UpdateGrantAsync(string sidHash, IdentityService.Application.Auth.SessionGrant grant, CancellationToken ct) => throw Down();

        public Task UpdateGrantForUserAsync(Guid userId, Guid tenantId, IdentityService.Application.Auth.SessionGrant grant, CancellationToken ct) => throw Down();

        public Task<IReadOnlyList<StoredSession>> ListForUserAsync(Guid userId, CancellationToken ct) => throw Down();

        public Task DeleteAsync(string sidHash, CancellationToken ct) => throw Down();

        public Task<bool> DeleteByPublicIdAsync(Guid userId, string publicId, CancellationToken ct) => throw Down();

        public Task DeleteAllForUserAsync(Guid userId, string? exceptSidHash, CancellationToken ct) => throw Down();
    }
}
