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

    public static AppException TransferCurrencyPair() {
        return new AppException(400, "transfer_currency_pair", "Farklı para birimli virmanda hesaplardan biri TRY olmalıdır.");
    }

    public static AppException TransferRateRequired() {
        return new AppException(400, "rate_required", "Hesapların para birimi farklı: kur girilmelidir.");
    }

    public static AppException CategoryKindMismatch(string message) {
        return new AppException(400, "category_kind_mismatch", message);
    }

    public static AppException CategoryInUse() {
        return new AppException(409, "category_in_use", "Belgede kullanılan kategori silinemez. Bunun yerine pasifleştirebilirsiniz.");
    }

    public static AppException CategorySystem() {
        return new AppException(409, "category_system", "Sistem kategorisi silinemez.");
    }

    public static AppException CategoryNameTaken(string name) {
        return new AppException(409, "name_taken", $"\"{name}\" kategorisi zaten var.");
    }

    public static AppException ExpenseTarget() {
        return new AppException(400, "expense_target", "Kasa/banka hesabı ya da cari verilmelidir; ikisi birden verilemez.");
    }

    public static AppException CariRole(string message) {
        return new AppException(400, "cari_role", message);
    }

    public static AppException AccountCurrencyMismatch(string message) {
        return new AppException(400, "account_currency_mismatch", message);
    }

    public static AppException InvalidTransition() {
        return new AppException(409, "invalid_instrument_transition", "Çek/senet bu duruma geçirilemez.");
    }

    public static AppException InstrumentNumberTaken() {
        return new AppException(409, "instrument_number_taken", "Bu numaralı çek/senet zaten kayıtlı.");
    }

    public static AppException InstrumentNotDue() {
        return new AppException(409, "instrument_not_due", "Verilen çek/senet vade tarihinden önce ödenemez.");
    }

    public static AppException DifferenceAlreadySettled() {
        return new AppException(409, "difference_already_settled", "Bu kur farkı daha önce işlenmiş.");
    }

    public static AppException DifferenceNotFound() {
        return new AppException(400, "difference_not_found", "Seçilen tahsislerde işlenebilir bir kur farkı bulunamadı.");
    }

    public static AppException DifferenceNotApplicable(string message) {
        return new AppException(400, "difference_not_applicable", message);
    }

    public static AppException VoucherUnbalanced() {
        return new AppException(400, "voucher_unbalanced", "Fişin toplam borcu toplam alacağına eşit olmalıdır.");
    }

    public static AppException VoucherInvalid(string message) {
        return new AppException(400, "voucher_invalid", message);
    }

    public static AppException VoucherCariCurrency() {
        return new AppException(400, "voucher_cari_currency", "Mahsup fişi satırına yalnızca TL carisi bağlanabilir.");
    }

    public static AppException VoucherSourceExists() {
        return new AppException(409, "voucher_source_exists", "Bu kaynak için zaten bir mahsup fişi var.");
    }

    public static AppException VoucherCancelled() {
        return new AppException(409, "voucher_cancelled", "Fiş zaten iptal edilmiş.");
    }

    public static AppException IdempotencyLost() {
        return new AppException(409, "idempotency_in_progress", "İdempotency kaydı başka bir istek tarafından devralındı; işlem geri alındı.");
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
