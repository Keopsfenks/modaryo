namespace ERP.Application.Common.Interfaces;

/// <summary>Kimlik servisindeki bir şubenin ERP'nin ihtiyaç duyduğu özeti.</summary>
public sealed record BranchRef(
    Guid   Id,
    string Code,
    string Name,
    bool   IsActive,
    bool   IsDefault
);

/// <summary>
///     Şubeler kimlik servisinde yaşar; ERP hangi şubenin var ve aktif olduğunu buradan öğrenir
///     (imzalı iç API, firma başına kısa süreli önbellek). Kimlik servisine ulaşılamazsa istisna fırlatır.
/// </summary>
public interface IBranchDirectory {
    Task<IReadOnlyList<BranchRef>> ListAsync(Guid tenantId, CancellationToken ct);

    Task<bool> ExistsActiveAsync(Guid tenantId, Guid branchId, CancellationToken ct);
}
