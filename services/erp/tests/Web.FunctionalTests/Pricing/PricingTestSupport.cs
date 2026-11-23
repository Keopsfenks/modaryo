using ERP.Application.Common.Interfaces;
using ERP.Domain.Pricing;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.FunctionalTests.Pricing;

public static class PricingTestSupport {
    public static ApplicationDbContext Db(Guid tenantId) {
        var tenant = new FakeTenant(tenantId, 0m);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(TestEnvironment.ConnectionString)
            .AddInterceptors(new TenantInterceptor(tenant))
            .Options;
        return new ApplicationDbContext(options, tenant);
    }

    public static async Task<Guid> AddListAsync(Guid tenantId, PriceListKind kind, string name = "Liste") {
        await using var db   = Db(tenantId);
        var             list = new PriceList { Name = name, Kind = kind };
        db.PriceLists.Add(list);
        await db.SaveChangesAsync();
        return list.Id;
    }

    public static async Task<Guid> AddGroupAsync(Guid tenantId, string name = "Bayiler") {
        await using var db    = Db(tenantId);
        var             group = new CustomerGroup { Name = name };
        db.CustomerGroups.Add(group);
        await db.SaveChangesAsync();
        return group.Id;
    }

    public sealed class FakeTenant(Guid? tenantId, decimal maxDiscount) : ICurrentTenant {
        public Guid?                TenantId           => tenantId;
        public Guid?                UserId             => null;
        public string?              UserFullName       => "Test Kullanıcı";
        public IReadOnlyList<Guid>? BranchIds          => null;
        public decimal              MaxDiscountPercent => maxDiscount;

        public bool HasModule(string key) {
            return true;
        }

        public bool CanAccessBranch(Guid branchId) {
            return true;
        }
    }
}
