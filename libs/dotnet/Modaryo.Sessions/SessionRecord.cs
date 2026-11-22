using System.Text.Json;

namespace Modaryo.Sessions;

/// <summary>
///     Valkey'de "sess:{sha256_hex(sid)}" anahtarında tutulan oturum kaydı (sözleşme v1).
///     Alan adları snake_case'tir; bkz. contracts/session-v1.example.json.
/// </summary>
public sealed record SessionRecord {
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) {
                                                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                                                        };

    public required int      V                 { get; init; }
    public required string   Id                { get; init; }
    public required Guid     UserId            { get; init; }
    public required string   Email             { get; init; }
    public required string   FullName          { get; init; }
    public          Guid?    TenantId          { get; init; }
    public required string[] Permissions       { get; init; }
    public          bool     RememberMe        { get; init; }
    public required long     CreatedAt         { get; init; }
    public required long     AbsoluteExpiresAt { get; init; }
    public required int      IdleTimeoutS      { get; init; }
    public          string?  Ip                { get; init; }
    public          string?  UserAgent         { get; init; }

    // Sözleşme v1'e sonradan eklenen alanlar; eski kayıtlarda yoktur ve varsayılanlarıyla okunur.

    /// <summary>"employee", "dealer" veya "self_service" (bkz. <see cref="SessionUserTypes" />).</summary>
    public string UserType { get; init; } = SessionUserTypes.Employee;

    /// <summary>Bayi / self-servis oturumlarında bağlı olduğu cari hesap.</summary>
    public Guid? CariId { get; init; }

    /// <summary>Kullanıcının erişebildiği modül anahtarları.</summary>
    public string[] Modules { get; init; } = [];

    /// <summary>Erişilebilen şubeler; null tüm şubeler, boş dizi hiçbir şube demektir.</summary>
    public Guid[]? BranchIds { get; init; }

    /// <summary>Kullanıcının uygulayabileceği en yüksek iskonto yüzdesi.</summary>
    public decimal MaxDiscountPct { get; init; }
}

public static class SessionUserTypes {
    public const string Employee    = "employee";
    public const string Dealer      = "dealer";
    public const string SelfService = "self_service";

    public static bool IsKnown(string? userType) {
        return userType is Employee or Dealer or SelfService;
    }
}