namespace ERP.Domain.Invoicing;

public class DocumentSeries : BaseEntity, ITenantOwned {
    /// <summary>"sales" | "purchase"</summary>
    public string      KindGroup  { get; set; } = "sales";
    public Guid        BranchId   { get; set; }
    public InvoiceMode Mode       { get; set; }
    public string      Prefix     { get; set; } = string.Empty;
    public int         Year       { get; set; }
    public long        NextNumber { get; set; } = 1;
    public int         MinWidth   { get; set; } = 6;
    public bool        IsDefault  { get; set; }
    public bool        IsActive   { get; set; } = true;
    public Guid        TenantId   { get; set; }

    public bool IsEBelge => Mode != InvoiceMode.Paper;

    /// <summary>
    ///     Numara biçimi. e-Belge: {önek}{yyyy}{9 haneli sıra} (16 karakter); kâğıt: önek + en az MinWidth haneli sıra.
    /// </summary>
    public string Format(long sequence) =>
        IsEBelge ? $"{Prefix}{Year:D4}{sequence:D9}" : $"{Prefix}{sequence.ToString().PadLeft(MinWidth, '0')}";
}
