namespace ERP.Domain.Catalog;

public class CustomFieldDefinition : BaseEntity, ITenantOwned {
    public string          Key              { get; set; } = string.Empty;
    public string          Label            { get; set; } = string.Empty;
    public CustomFieldType Type             { get; set; }
    public string[]        Options          { get; set; } = [];
    public bool            Required         { get; set; }
    public bool            AppliesToProduct { get; set; } = true;
    public bool            AppliesToService { get; set; } = true;
    public int             Sort             { get; set; }
    public Guid            TenantId         { get; set; }
}
