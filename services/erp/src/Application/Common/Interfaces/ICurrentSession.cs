namespace ERP.Application.Common.Interfaces;

/// <summary>İsteği yapan oturum (Valkey'deki kayıttan). Oturum yoksa UserId null.</summary>
public interface ICurrentSession
{
    Guid? UserId { get; }

    Guid? TenantId { get; }

    string? SidHash { get; }

    /// <summary>Aktif oturumlar ekranındaki kimlik.</summary>
    string? SessionId { get; }

    IReadOnlyList<string> Permissions { get; }

    bool HasPermission(string permission);

    string? Ip { get; }

    string? UserAgent { get; }
}
