namespace IdentityService.Domain.Entities;

/// <summary>Firma (kiracı).</summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }
}
