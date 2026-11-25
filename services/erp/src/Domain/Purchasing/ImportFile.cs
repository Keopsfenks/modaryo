namespace ERP.Domain.Purchasing;

public class ImportFile : BaseAuditableEntity, ITenantOwned {
    public string           Number        { get; set; } = string.Empty;
    public ImportFileStatus Status        { get; set; } = ImportFileStatus.Open;
    public Guid             SupplierId    { get; set; }
    public string           OriginCountry { get; set; } = string.Empty;
    public Incoterm         Incoterm      { get; set; } = Incoterm.FOB;
    public string           Currency      { get; set; } = "USD";
    public string?          Notes         { get; set; }

    public List<ImportFileLink> Links { get; set; } = new();

    public Guid TenantId { get; set; }
}

/// <summary>İthalat dosyasının bağlı belgesi ya da eki (P0b dosyası).</summary>
public class ImportFileLink : BaseEntity, ITenantOwned {
    public Guid           ImportFileId { get; set; }
    public ImportLinkKind Kind         { get; set; }
    public Guid           RefId        { get; set; }
    public Guid           TenantId     { get; set; }
}
