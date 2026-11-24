using ERP.Application.Common.Exceptions;

namespace ERP.Application.Invoicing;

public static class InvoicingPermissions {
    public const string Read              = "fatura.read";
    public const string Manage            = "fatura.manage";
    public const string Issue             = "fatura.issue";
    public const string Cancel            = "fatura.cancel";
    public const string SettingsDocuments = "ayarlar.belgeler";
    public const string SettingsEBelge    = "ayarlar.ebelge";
    public const string RateManage        = "kur.manage";
}

public static class InvoicingErrors {
    public static AppException SeriesMissing() {
        return new AppException(409, "series_missing", "Bu şube ve belge türü için varsayılan bir seri tanımlı değil.");
    }

    public static AppException SeriesExists(string prefix) {
        return new AppException(409, "series_exists", $"\"{prefix}\" öneki bu yıl için aynı türde başka bir seride kullanılıyor.");
    }

    public static AppException PrefixTaken(string prefix) {
        return new AppException(409, "prefix_taken", $"\"{prefix}\" öneki firmada başka bir seri (belge grubu, mod ya da şube) tarafından kullanılıyor; numaralar firma genelinde tekildir.");
    }

    public static AppException SeriesExhausted(string prefix) {
        return new AppException(409, "series_exhausted", $"\"{prefix}\" serisinin yıllık numara sınırı doldu; yeni bir önek tanımlayın.");
    }

    /// <summary>(Firma, önek, yıl) tekilliğini sağlayan indeksin adı.</summary>
    public const string PrefixYearIndex = "IX_document_series_prefix_year";

    public static AppException SeriesInUse() {
        return new AppException(409, "series_in_use", "Numara alınmış bir serinin öneki ve yılı değiştirilemez.");
    }

    public static AppException BranchNotFound() {
        return new AppException(404, "branch_not_found", "Şube bulunamadı ya da pasif.");
    }

    /// <summary>(Kiracı, şube, grup, mod) başına tek varsayılan seri sağlayan kısmi tekil indeksin adı.</summary>
    public const string DefaultSeriesIndex = "IX_document_series_default";

    public static AppException SeriesDefaultConflict() {
        return new AppException(409, "series_default_conflict", "Varsayılan seri aynı anda başka biri tarafından değiştirildi. Tekrar deneyin.");
    }

    /// <summary>İhlal edilen kısıtın adı (PostgresException.ConstraintName).</summary>
    public static string? ConstraintName(DbUpdateException ex) {
        return ex.InnerException?.GetType().GetProperty("ConstraintName")?.GetValue(ex.InnerException) as string;
    }

    /// <summary>Benzersiz indeks çakışması (yarış durumu) mı?</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) {
        var inner = ex.InnerException;
        return inner is not null && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";
    }
}
