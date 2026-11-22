using System.Text.Json;

namespace Modaryo.Sessions;

/// <summary>
/// Valkey'de "sess:{sha256_hex(sid)}" anahtarında tutulan oturum kaydı (sözleşme v1).
/// Alan adları snake_case'tir; bkz. docs/contracts/session-v1.example.json.
/// </summary>
public sealed record SessionRecord
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public required int V { get; init; }
    public required string Id { get; init; }
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string FullName { get; init; }
    public Guid? TenantId { get; init; }
    public required string[] Permissions { get; init; }
    public bool RememberMe { get; init; }
    public required long CreatedAt { get; init; }
    public required long AbsoluteExpiresAt { get; init; }
    public required int IdleTimeoutS { get; init; }
    public string? Ip { get; init; }
    public string? UserAgent { get; init; }
}
