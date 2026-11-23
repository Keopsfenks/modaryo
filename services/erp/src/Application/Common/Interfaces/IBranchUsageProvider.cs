namespace ERP.Application.Common.Interfaces;

/// <summary>
///     Bir şubenin ERP kayıtlarında kullanılıp kullanılmadığını söyler. Şube verisi taşıyan her modül kendi
///     uygulamasını kaydeder; kimlik servisi şube silmeden önce bunları sorar. Hiç kayıt yoksa şube kullanımda değildir.
/// </summary>
public interface IBranchUsageProvider {
    Task<bool> IsInUseAsync(Guid tenantId, Guid branchId, CancellationToken ct);
}
