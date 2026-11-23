namespace IdentityService.Application.Common.Interfaces;

/// <summary>
/// ERP'ye şubenin ERP kayıtlarında kullanılıp kullanılmadığını sorar (imzalı iç API).
/// ERP'ye ulaşılamazsa, zaman aşımında, 2xx dışı veya bozuk yanıtta 503 erp_unavailable fırlatır (güvenli tarafta kalır).
/// </summary>
public interface IErpBranchUsageClient {
    Task<bool> IsInUseAsync(Guid tenantId, Guid branchId, CancellationToken ct);
}
