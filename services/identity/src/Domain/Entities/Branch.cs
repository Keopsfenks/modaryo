namespace IdentityService.Domain.Entities;

/// <summary>Firma şubesi. Her firmanın tam bir varsayılan şubesi (Merkez / MRK) vardır.</summary>
public class Branch : BaseEntity
{
    public Guid TenantId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
