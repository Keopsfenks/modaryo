using ERP.Application.Common.Exceptions;

namespace ERP.Application.Inventory;

public static class InventoryPermissions {
    public const string Read     = "stok.read";
    public const string Manage   = "stok.manage";
    public const string Count    = "stok.sayim";
    public const string Cost     = "maliyet.read";
    public const string Settings = "ayarlar.stok";
}

public static class InventoryErrors {
    public static AppException WarehouseInUse() {
        return new AppException(409, "warehouse_in_use", "Hareketi olan depo silinemez. Bunun yerine pasifleştirebilirsiniz.");
    }

    public static AppException WarehouseIsDefault() {
        return new AppException(409, "warehouse_is_default",
                                "Şubenin varsayılan deposu silinemez, pasifleştirilemez ve türü değiştirilemez.");
    }

    public static AppException CodeTaken(string code) {
        return new AppException(409, "code_taken", $"\"{code}\" kodu bu firmada başka bir depoda kullanılıyor.");
    }

    public static AppException BranchNotFound() {
        return new AppException(404, "branch_not_found", "Şube bulunamadı ya da pasif.");
    }

    public static AppException CostingMethodLocked() {
        return new AppException(409, "costing_method_locked",
                                "Stok hareketi girildikten sonra maliyet yöntemi değiştirilemez.");
    }

    public static AppException BundleNested() {
        return new AppException(409, "bundle_nested", "Set ürün başka bir set ürünü bileşen olarak içeremez.");
    }

    public static AppException TrackingModeLocked() {
        return new AppException(409, "tracking_mode_locked",
                                "Stok hareketi olan ürünün seri/lot takip türü değiştirilemez.");
    }

    /// <summary>Kayıt sırasında benzersiz indeks çakışması (yarış durumu) mı?</summary>
    public static bool IsUniqueViolation(Microsoft.EntityFrameworkCore.DbUpdateException ex) {
        var inner = ex.InnerException;
        return inner is not null && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";
    }
}
