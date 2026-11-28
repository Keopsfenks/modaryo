using ERP.Application.Common.Exceptions;

namespace ERP.Application.Shipping;

public static class ShippingPermissions {
    public const string Read     = "sevkiyat.read";
    public const string Pick     = "sevkiyat.pick";
    public const string Pack     = "sevkiyat.pack";
    public const string Ship     = "sevkiyat.ship";
    public const string Deliver  = "sevkiyat.deliver";
    public const string Plan     = "sevkiyat.plan";
    public const string Settings = "ayarlar.sevkiyat";

    /// <summary>Sevk oluşturma/iptal: P7 satış sevk izni ya da sevkiyat sevk izni yeterlidir (AuthorizationBehaviour "a|b" = VEYA).</summary>
    public const string ShipOrSalesShip = "satis.ship|sevkiyat.ship";

    /// <summary>Sevk listesi/detayı: satış okuma ya da sevkiyat okuma izni yeterlidir.</summary>
    public const string ReadOrSalesRead = "satis.read|sevkiyat.read";

    /// <summary>Toplamayı geri alma: toplama ya da paketleme izni yeterlidir.</summary>
    public const string PickOrPack = "sevkiyat.pick|sevkiyat.pack";
}

public static class ShippingErrors {
    public static AppException InvalidTransition(string from, string to) {
        return new AppException(409, "invalid_shipment_transition", $"Sevk \"{from}\" durumundan \"{to}\" durumuna geçirilemez.");
    }

    public static AppException PickIncomplete() {
        return new AppException(409, "pick_incomplete", "Toplama tamamlanmadı: işlenmemiş satırlar var.");
    }

    public static AppException ParcelRequired() {
        return new AppException(409, "parcel_required", "Paketleme için en az bir koli girilmelidir.");
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
