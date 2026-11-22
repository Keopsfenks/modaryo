namespace IdentityService.Domain.Entities;

/// <summary>Firma (kiracı).</summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    /// <summary>Açık modül anahtarları (ModuleKeys). Yeni firmada boştur.</summary>
    public List<string> EnabledModules { get; set; } = [];

    /// <summary>Firmaya en son uygulanan hazır rol izin seti sürümü.</summary>
    public int AppliedPermissionSetVersion { get; set; }
}
