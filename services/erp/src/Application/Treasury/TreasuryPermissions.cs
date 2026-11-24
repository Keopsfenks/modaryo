using ERP.Application.Common.Exceptions;

namespace ERP.Application.Treasury;

public static class TreasuryPermissions {
    public const string Read       = "finans.read";
    public const string Manage     = "finans.manage";
    public const string Collection = "tahsilat.manage";
    public const string Payment    = "odeme.manage";
    public const string Transfer   = "virman.manage";
    public const string Expense    = "masraf.manage";
    public const string Instrument = "ceksenet.manage";
    public const string Journal    = "muhasebe.manage";
    public const string Export     = "muhasebe.export";
}

public static class TreasuryErrors {
    public static AppException AccountInUse() {
        return new AppException(409, "account_in_use", "Hareketi olan hesap silinemez. Bunun yerine pasifleştirebilirsiniz.");
    }

    public static AppException NameTaken(string name) {
        return new AppException(409, "name_taken", $"\"{name}\" adı bu firmada başka bir hesapta kullanılıyor.");
    }

    public static AppException CurrencyLocked() {
        return new AppException(409, "currency_locked", "Hareketi olan hesabın para birimi ve türü değiştirilemez.");
    }

    public static AppException BranchNotFound() {
        return new AppException(404, "branch_not_found", "Şube bulunamadı ya da pasif.");
    }

    public static AppException AccountInactive() {
        return new AppException(409, "account_inactive", "Pasif hesaba hareket girilemez.");
    }

    public static AppException AccountMethodMismatch(string message) {
        return new AppException(400, "account_method_mismatch", message);
    }

    public static AppException RateRequired() {
        return new AppException(400, "rate_required", "Hesap ve cari para birimi farklı: kur girilmeli (bu tarih için TCMB kuru da bulunamadı).");
    }

    public static AppException AllocationExceedsOpen(string message) {
        return new AppException(400, "allocation_exceeds_open", message);
    }

    public static AppException AllocationOtherAccount() {
        return new AppException(400, "allocation_other_account", "Eşleştirilen satırlar aynı cariye ait olmalıdır.");
    }

    public static AppException AllocationInvalid(string message) {
        return new AppException(400, "allocation_invalid", message);
    }

    public static AppException AllocationDirection() {
        return new AppException(400, "allocation_direction", "Eşleştirme borç ve alacak satırları arasında yapılabilir; aynı yöndeki satırlar eşleştirilemez.");
    }

    public static AppException AllocationLineCancelled() {
        return new AppException(409, "allocation_line_cancelled", "İptal edilmiş bir hareketin satırı eşleştirilemez.");
    }

    public static AppException AllocationNotManual() {
        return new AppException(409, "allocation_not_manual", "Yalnızca elle yapılan eşleştirmeler kaldırılabilir.");
    }

    public static AppException DocumentCancelled() {
        return new AppException(409, "document_cancelled", "Belge zaten iptal edilmiş.");
    }

    public static AppException DocumentKindUnsupported() {
        return new AppException(409, "document_kind_unsupported", "Bu belge türü buradan iptal edilemez.");
    }

    public static AppException ModuleDisabled() {
        return new AppException(403, "module_disabled", "Bu modül firmanızda kapalı.");
    }

    /// <summary>Kayıt sırasında benzersiz indeks çakışması (yarış durumu) mı?</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) {
        var inner = ex.InnerException;
        return inner is not null && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";
    }
}
