namespace ERP.Domain.Catalog;

/// <summary>Excel içe aktarma önizlemesi: satırlar uygulanana dek burada saklanır, 30 dakika sonra geçersiz olur.</summary>
public class ImportBatch : BaseEntity, ITenantOwned {
    public string          Kind        { get; set; } = "products";
    public string          RowsJson    { get; set; } = "{}";
    public DateTimeOffset  CreatedAt   { get; set; }
    public Guid            CreatedBy   { get; set; }
    public DateTimeOffset  ExpiresAt   { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }

    public Guid TenantId { get; set; }
}
