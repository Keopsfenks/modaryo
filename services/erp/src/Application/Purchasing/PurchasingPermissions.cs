using ERP.Application.Common.Exceptions;

namespace ERP.Application.Purchasing;

public static class PurchasingPermissions {
    public const string Read       = "satinalma.read";
    public const string Manage     = "satinalma.manage";
    public const string Receive    = "satinalma.receive";
    public const string Quarantine = "satinalma.quarantine";
    public const string Settings   = "ayarlar.satinalma";

    /// <summary>Alış fiyatlarını görme yetkisi (fiyatlandırma modülünden).</summary>
    public const string PurchasePrice = "fiyat.alis";
}

public static class PurchasingErrors {
    public static AppException PoNotDraft() {
        return new AppException(409, "po_not_draft", "Yalnızca taslak siparişler değiştirilebilir.");
    }

    public static AppException PoHasReceipts() {
        return new AppException(409, "po_has_receipts", "Mal kabulü olan sipariş iptal edilemez; kalan miktarı kapatabilirsiniz.");
    }

    public static AppException OverDelivery() {
        return new AppException(409, "over_delivery", "Gelen miktar sipariş miktarını ve izin verilen toleransı aşıyor.");
    }

    public static AppException ReceiptExceedsOpen() {
        return new AppException(409, "receipt_exceeds_open", "Gelen miktar siparişin açık miktarını aşıyor.");
    }

    public static AppException WeightMissing(string message) {
        return new AppException(409, "weight_missing", message);
    }

    public static AppException LandedCostTargetCancelled() {
        return new AppException(409, "landed_cost_target_cancelled", "İptal edilmiş mal kabule masraf dağıtılamaz.");
    }

    public static AppException QuarantineNotEnabled() {
        return new AppException(409, "quarantine_not_enabled", "Karantina bu firmada etkin değil.");
    }

    public static AppException ModuleDisabled() {
        return new AppException(403, "module_disabled", "Bu modül firmanızda kapalı.");
    }

    public static AppException Conflict(string code, string message) {
        return new AppException(409, code, message);
    }

    public static AppException Bad(string code, string message) {
        return new AppException(400, code, message);
    }

    public static AppException VersionConflict() {
        return new AppException(409, "version_conflict", "Sipariş başkası tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin.");
    }
}
