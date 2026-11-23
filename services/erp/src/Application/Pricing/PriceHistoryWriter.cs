using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Pricing;

namespace ERP.Application.Pricing;

/// <summary>
///     Fiyat değişikliklerini geçmişe yazar. Kaydı yalnızca bağlama ekler; çağıran SaveChanges ile aynı işlemde
///     kaydeder (satır ve geçmişi birlikte işlenir ya da birlikte geri alınır).
/// </summary>
public sealed class PriceHistoryWriter(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const string ScopeList     = "list";
    public const string ScopeCustomer = "customer";
    public const string ScopeCard     = "card";

    /// <summary>before/after: kademe listesi (liste, müşteri) ya da Money4 (kart); null "yoktu/silindi" demektir.</summary>
    public void Record(string        scope,
                       Guid          scopeId,
                       Guid          productId,
                       Guid?         variantId,
                       string        unit,
                       object?       before,
                       object?       after,
                       PriceListKind kind
    ) {
        db.PriceHistory.Add(new PriceHistoryEntry {
            At         = DateTimeOffset.UtcNow,
            ActorLabel = string.IsNullOrWhiteSpace(tenant.UserFullName) ? "Sistem" : tenant.UserFullName!,
            Scope      = scope,
            ScopeId    = scopeId,
            ProductId  = productId,
            VariantId  = variantId,
            Unit       = unit,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, Json),
            AfterJson  = after is null ? null : JsonSerializer.Serialize(after, Json),
            Kind       = kind
        });
    }
}
