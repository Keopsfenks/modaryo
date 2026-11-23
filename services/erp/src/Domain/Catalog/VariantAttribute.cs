namespace ERP.Domain.Catalog;

public class VariantAttribute {
    public Guid         Id     { get; set; } = Guid.CreateVersion7();
    public string       Name   { get; set; } = string.Empty;
    public List<string> Values { get; set; } = new();
    public int          Sort   { get; set; }
}
