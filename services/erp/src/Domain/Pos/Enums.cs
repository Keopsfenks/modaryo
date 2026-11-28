namespace ERP.Domain.Pos;

/// <summary>Yasal belge modu: yazar kasa fişi ("yazar kasam var") veya e-Arşiv fatura ("yazar kasam yok").</summary>
public enum LegalDocumentMode { FiscalReceipt, EArchiveInvoice }

public enum PosPaymentMethod { Cash, Card, OnAccount }

public enum ScaleValueKind { WeightGrams, PriceKurus }

public enum ShiftStatus { Open, Closed }

public enum CashMoveDirection { In, Out }

public enum PosSaleStatus { Completed, Cancelled }

/// <summary>POS iş kuralı ihlali. Kod, uygulama katmanında AppException koduna çevrilir.</summary>
public sealed class PosRuleException(string code, string message) : Exception(message) {
    public string Code { get; } = code;
}
