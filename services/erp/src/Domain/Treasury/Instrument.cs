namespace ERP.Domain.Treasury;

/// <summary>Çek veya senet; alınan (müşteri) ya da verilen (firma) olabilir.</summary>
public class Instrument : BaseAuditableEntity, ITenantOwned {
    public InstrumentKind      Kind             { get; set; }
    public InstrumentDirection Direction        { get; set; }
    public InstrumentStatus    Status           { get; set; }
    public string              Number           { get; set; } = string.Empty;
    public DateOnly            DueDate          { get; set; }
    public decimal             Amount           { get; set; }
    public string              Currency         { get; set; } = "TRY";
    public string              DrawerName       { get; set; } = string.Empty;
    public string?             BankName         { get; set; }
    public string?             BankBranch       { get; set; }
    public string?             AccountNo        { get; set; }
    public int                 EndorsementCount { get; set; }

    /// <summary>Asıl cari: alınanda müşteri, verilende tedarikçi.</summary>
    public Guid CurrentAccountId { get; set; }

    /// <summary>Ciro edildiyse ciro edilen tedarikçi cari.</summary>
    public Guid? EndorsedToId { get; set; }

    /// <summary>Tahsile verildiyse tahsil hesabı (banka).</summary>
    public Guid? CollectionAccountId { get; set; }

    public bool ProtestFlag { get; set; }
    public Guid TenantId    { get; set; }
}
