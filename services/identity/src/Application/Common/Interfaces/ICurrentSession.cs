namespace IdentityService.Application.Common.Interfaces;

/// <summary>İsteği yapan oturum (Valkey'deki kayıttan). Oturum yoksa UserId null.</summary>
public interface ICurrentSession
{
    Guid? UserId { get; }

    Guid? TenantId { get; }

    /// <summary>Oturum kaydındaki ad (denetim kaydında işlemi yapan olarak yazılır).</summary>
    string? FullName { get; }

    string? SidHash { get; }

    /// <summary>Aktif oturumlar ekranındaki kimlik.</summary>
    string? SessionId { get; }

    IReadOnlyList<string> Permissions { get; }

    bool HasPermission(string permission);

    /// <summary>Oturumun erişebildiği şubeler; null tüm şubeler demektir.</summary>
    IReadOnlyList<Guid>? BranchIds { get; }

    /// <summary>Oturumun iskonto sınırı (yüzde).</summary>
    decimal MaxDiscountPercent { get; }

    string? Ip { get; }

    string? UserAgent { get; }
}
