using ERP.Application.Common.Exceptions;

namespace ERP.Application.Sales;

public static class SalesPermissions {
    public const string Read          = "satis.read";
    public const string Manage        = "satis.manage";
    public const string Approve       = "satis.approve";
    public const string LimitOverride = "satis.limit-asimi";
    public const string Ship          = "satis.ship";
    public const string Return        = "satis.return";
    public const string Recurring     = "satis.recurring";
    public const string Targets       = "satis.targets";
    public const string Settings      = "ayarlar.satis";

    /// <summary>Tahmini maliyet / kâr önizlemesi için stok modülünün maliyet yetkisi.</summary>
    public const string Cost = "maliyet.read";
}

public static class SalesErrors {
    public static AppException QuoteNotEditable() {
        return new AppException(409, "quote_not_editable", "Bu teklif artık değiştirilemez.");
    }

    public static AppException OrderNotDraft() {
        return new AppException(409, "order_not_draft", "Yalnızca taslak siparişler değiştirilebilir.");
    }

    public static AppException SalesRepRequired() {
        return new AppException(400, "sales_rep_required", "Satış temsilcisi seçilmelidir.");
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
        return new AppException(409, "version_conflict", "Belge başkası tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin.");
    }
}
