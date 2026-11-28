namespace ERP.Domain.Pos;

public enum PosReturnRefundMethod { Cash, Card }

/// <summary>Fiş numarası üzerinden iade. Değişim, iade + yeni satışın tek istekte yapılmasıdır.</summary>
public class PosReturn : BaseAuditableEntity, ITenantOwned {
    public string                Number       { get; set; } = string.Empty;
    public Guid                  SaleId       { get; set; }
    public Guid                  ShiftId      { get; set; }
    public Guid                  RegisterId   { get; set; }
    public DateOnly              Date         { get; set; }
    public DateTimeOffset        At           { get; set; }
    public decimal               RefundAmount { get; set; }
    public PosReturnRefundMethod RefundMethod { get; set; }
    public Guid                  CashierUserId { get; set; }
    public LegalDocumentMode     Mode          { get; set; } = LegalDocumentMode.FiscalReceipt;

    /// <summary>İade edilen malın KDV hariç ve KDV tutarları; toplam = Net + Vat = iade değeri.</summary>
    public decimal Net { get; set; }
    public decimal Vat { get; set; }

    /// <summary>İade değerinin dağılımı: kasadan nakit, karta iade, veresiye borcundan düşülen ve değişimde yeni satışa mahsup edilen.</summary>
    public decimal CashRefund      { get; set; }
    public decimal CardRefund      { get; set; }
    public decimal OnAccountCredit { get; set; }
    public decimal ExchangeCredit  { get; set; }

    /// <summary>Değişimde bağlı yeni satış.</summary>
    public Guid? ExchangeSaleId { get; set; }

    /// <summary>e-Arşiv modunda oluşan SalesReturn tipi fatura (taslak).</summary>
    public Guid? ReturnInvoiceId { get; set; }

    public string? Reason { get; set; }

    /// <summary>İstemcinin ürettiği (isteğe bağlı) tek seferlik iade kimliği: aynı kimlikli yeniden gönderim yeni iade yazmaz.</summary>
    public Guid? ClientReturnId { get; set; }

    public string? CashierName { get; set; }

    public List<PosReturnLine> Lines { get; set; } = new();

    public uint Version  { get; set; }
    public Guid TenantId { get; set; }
}

public class PosReturnLine : BaseEntity, ITenantOwned {
    public Guid    ReturnId   { get; set; }
    public Guid    SaleLineId { get; set; }
    public decimal Quantity   { get; set; }
    public decimal Amount     { get; set; }
    public decimal Net        { get; set; }
    public decimal Vat        { get; set; }
    public Guid    TenantId   { get; set; }
}
