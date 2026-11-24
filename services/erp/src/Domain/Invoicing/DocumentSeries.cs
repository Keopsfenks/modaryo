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

    /// <summary>Sıra 9 haneye sığdığı sürece numara 16 karakterdir.</summary>
    public const long MaxSequence = 999_999_999;

    /// <summary>
    ///     Numara biçimi (tüm modlarda aynı): {3 karakter önek}{yyyy}{9 haneli sıra} = 16 karakter (örn. ABC2026000000001),
    ///     e-Fatura biçimine uygun. Yıl serinin yılıdır; yeni yılda aynı önekle yeni bir seri satırı açılır ve sıra 1'den başlar.
    /// </summary>
    public string Format(long sequence) => $"{Prefix}{Year:D4}{sequence:D9}";
}
