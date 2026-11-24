using ERP.Application.Common.Interfaces;

namespace ERP.Application.Treasury;

/// <summary>Şubede kasa/banka hesabı varsa şube kullanımdadır (hesap şubeye bağlıdır).</summary>
public sealed class TreasuryBranchUsageProvider(IApplicationDbContext db) : IBranchUsageProvider {
    public async Task<bool> IsInUseAsync(Guid tenantId, Guid branchId, CancellationToken ct) {
        return await db.TreasuryAccounts.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenantId && a.BranchId == branchId, ct);
    }
}
