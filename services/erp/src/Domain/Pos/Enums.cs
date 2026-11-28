namespace ERP.Domain.Pos;

/// <summary>Yasal belge modu: yazar kasa fişi ("yazar kasam var") veya e-Arşiv fatura ("yazar kasam yok").</summary>
public enum LegalDocumentMode { FiscalReceipt, EArchiveInvoice }

/// <summary>ReturnCredit: değişimde iade edilen mal bedelinin yeni satışa mahsup edilen kısmı (kasaya nakit/kart girmez).</summary>
public enum PosPaymentMethod { Cash, Card, OnAccount, ReturnCredit }

public enum ScaleValueKind { WeightGrams, PriceKurus }

public enum ShiftStatus { Open, Closed }

public enum CashMoveDirection { In, Out }

public enum PosSaleStatus { Completed, Cancelled }

/// <summary>POS iş kuralı ihlali. Kod, uygulama katmanında AppException koduna çevrilir.</summary>
public sealed class PosRuleException(string code, string message) : Exception(message) {
    public string Code { get; } = code;
}
