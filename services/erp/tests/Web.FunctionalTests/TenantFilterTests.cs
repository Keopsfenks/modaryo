using ERP.Application.Common.Interfaces;
using ERP.Domain.Common;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Web.FunctionalTests;

/// <summary>
///     Firma ayrımı: ITenantOwned varlıklar yalnızca aktif firmada görünür ve yazılır. Üretim modelinde henüz böyle bir
///     varlık olmadığından, ApplicationDbContext'ten türeyen test bağlamına test varlığı eklenir (ayrı geçici veritabanı).
/// </summary>
public class TenantFilterTests {
    private static readonly Guid   TenantA           = Guid.CreateVersion7();
    private static readonly Guid   TenantB           = Guid.CreateVersion7();
    private                 string _connectionString = null!;
    private                 string _databaseName     = null!;

    private TestDbContext Context(Guid? tenantId) {
        var tenant = new FixedTenant(tenantId);
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_connectionString)
            .AddInterceptors(new TenantInterceptor(tenant))
            .Options;
        return new TestDbContext(options, tenant);
    }

    [OneTimeSetUp]
    public async Task CreateDatabase() {
        _databaseName     = $"erp_filter_test_{Guid.NewGuid():N}";
        _connectionString = $"{TestEnvironment.ServerConnection};Database={_databaseName}";
        await using var context = Context(null);
        await context.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task DropDatabase() {
        NpgsqlConnection.ClearAllPools();
        await TestEnvironment.ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    [Test]
    public async Task TenantFilter_HidesOtherTenantsRows_AndStampsNewRows() {
        await using (var a = Context(TenantA)) {
            a.Records.Add(new TestRecord { Name = "A'nın kaydı" });
            await a.SaveChangesAsync();
        }

        await using (var b = Context(TenantB)) {
            b.Records.Add(new TestRecord { Name = "B'nin kaydı" });
            await b.SaveChangesAsync();
        }

        await using var readA    = Context(TenantA);
        await using var readB    = Context(TenantB);
        await using var noTenant = Context(null);

        (await readA.Records.Select(r => r.Name).ToListAsync()).ShouldBe(["A'nın kaydı"]);
        (await readB.Records.Select(r => r.Name).ToListAsync()).ShouldBe(["B'nin kaydı"]);
        (await noTenant.Records.CountAsync()).ShouldBe(0);
        (await readA.Records.SingleAsync()).TenantId.ShouldBe(TenantA);
    }

    [Test]
    public async Task Save_WithoutTenant_Throws() {
        await using var context = Context(null);
        context.Records.Add(new TestRecord { Name = "sahipsiz" });

        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Test]
    public async Task Save_IntoOtherTenant_Throws() {
        await using var context = Context(TenantA);
        context.Records.Add(new TestRecord { Name = "başkasına", TenantId = TenantB });

        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    public sealed class TestRecord : ITenantOwned {
        public Guid Id { get; set; } = Guid.CreateVersion7();

        public string Name { get; set; } = string.Empty;

        public Guid TenantId { get; set; }
    }

    private sealed class FixedTenant(
        Guid? tenantId
    ) : ICurrentTenant {
        public Guid?                  TenantId           => tenantId;
        public Guid?                  UserId             => null;
        public string?                UserFullName       => null;
        public IReadOnlyList<Guid>?   BranchIds          => [];
        public decimal                MaxDiscountPercent => 0m;

        public bool HasModule(string key) {
            return false;
        }

        public bool CanAccessBranch(Guid branchId) {
            return false;
        }
    }

    private sealed class TestDbContext(
        DbContextOptions<TestDbContext> options,
        ICurrentTenant                  tenant
    ) : ApplicationDbContext(options, tenant) {
        public DbSet<TestRecord> Records => Set<TestRecord>();

        protected override void OnModelCreating(ModelBuilder builder) {
            builder.Entity<TestRecord>().ToTable("test_records");
            base.OnModelCreating(builder);
        }
    }
}