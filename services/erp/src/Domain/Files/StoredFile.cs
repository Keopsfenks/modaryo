namespace ERP.Domain.Files;

/// <summary>
///     Yüklenen dosyanın kaydı; içerik nesne deposunda (ObjectKey) durur. İçerik türü ve uzantı istemcinin
///     söylediğinden değil dosyanın içeriğinden (sihirli baytlar) gelir.
/// </summary>
public class StoredFile : BaseEntity, ITenantOwned {
    public Guid TenantId { get; set; }

    /// <summary>logo | attachment | image</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>İstemcinin verdiği ad; yol parçaları atılmış, en fazla 255 karakter.</summary>
    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    public string ObjectKey { get; set; } = string.Empty;

    /// <summary>Küçük harf onaltılık SHA-256.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
