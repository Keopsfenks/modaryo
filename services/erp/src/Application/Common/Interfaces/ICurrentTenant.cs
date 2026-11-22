namespace ERP.Application.Common.Interfaces;

/// <summary>
///     İsteği yapan oturumun bağlamı (Valkey'deki oturum kaydından): aktif firma, kullanıcı, açık modüller,
///     erişilebilir şubeler ve iskonto sınırı. Oturum yoksa kimlikler null, modül yok, şube yok (güvenli varsayılan).
/// </summary>
public interface ICurrentTenant {
    Guid? TenantId { get; }

    Guid? UserId { get; }

    string? UserFullName { get; }

    /// <summary>Modül anahtarı firmada açık mı? Yalnızca aç/kapa modüller anahtar taşır (pos, sube, ...).</summary>
    bool HasModule(string key);

    /// <summary>Erişilebilir şubeler: null tüm şubeler, boş liste hiçbir şube.</summary>
    IReadOnlyList<Guid>? BranchIds { get; }

    bool CanAccessBranch(Guid branchId);

    /// <summary>Kullanıcının verebileceği en yüksek iskonto yüzdesi.</summary>
    decimal MaxDiscountPercent { get; }
}