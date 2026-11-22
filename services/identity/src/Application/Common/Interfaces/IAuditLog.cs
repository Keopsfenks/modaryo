namespace IdentityService.Application.Common.Interfaces;

/// <summary>
/// Denetim kaydı. Kaydı yalnızca DbContext'e ekler; çağıran, değişikliğin kendisiyle aynı SaveChanges içinde kaydeder.
/// Böylece değişiklik başarısız olursa denetim satırı da oluşmaz.
/// </summary>
public interface IAuditLog
{
    void Record(string action, string entityType, string entityId, object? before, object? after);
}
