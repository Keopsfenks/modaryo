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

    public static AppException ModuleDisabled() {
        return new AppException(403, "module_disabled", "Bu modül firmanızda kapalı.");
    }

    /// <summary>Kayıt sırasında benzersiz indeks çakışması (yarış durumu) mı?</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) {
        var inner = ex.InnerException;
        return inner is not null && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";
    }
}
