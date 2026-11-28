using ERP.Application.Settings.Categories;

namespace ERP.Application.Shipping.Dispatch;

/// <summary>Kargo firması kuralları: ayarlardaki listeden doğrulama, takip numarası zorunluluğu ve takip bağlantısı.</summary>
public static class CarrierRules {
    /// <summary>Ayarlardaki etkin firma; kod boşsa ya da listede yoksa 400 carrier_invalid.</summary>
    public static CarrierInfo Resolve(ShippingSettings settings, string? code) {
        var carrier = (settings.Carriers ?? ShippingSettings.CarrierSeed).FirstOrDefault(c => c.IsActive && string.Equals(c.Code, code?.Trim(), StringComparison.Ordinal));
        return carrier ?? throw ShippingErrors.Bad("carrier_invalid", "Kargo firması geçersiz ya da pasif.");
    }

    /// <summary>Kendi aracımız dışındaki firmalarda takip numarası zorunludur (409 tracking_required).</summary>
    public static void EnsureTracking(CarrierInfo carrier, string? trackingNo) {
        if (carrier.Code != ShippingSettings.OwnVehicleCode && string.IsNullOrWhiteSpace(trackingNo))
            throw ShippingErrors.Conflict("tracking_required", $"{carrier.Name} ile gönderimde takip numarası zorunludur.");
    }

    /// <summary>Firmanın bağlantı şablonundaki {no} yerine takip numarası (URL kodlanmış) konur; şablon ya da numara yoksa null.</summary>
    public static string? TrackingUrl(ShippingSettings settings, string? code, string? trackingNo) {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(trackingNo)) return null;
        var carrier = (settings.Carriers ?? ShippingSettings.CarrierSeed).FirstOrDefault(c => c.Code == code);
        return string.IsNullOrWhiteSpace(carrier?.TrackingUrlTemplate) ? null : carrier.TrackingUrlTemplate.Replace("{no}", Uri.EscapeDataString(trackingNo.Trim()), StringComparison.Ordinal);
    }

    public static string? Name(ShippingSettings settings, string? code) {
        return code is null ? null : (settings.Carriers ?? ShippingSettings.CarrierSeed).FirstOrDefault(c => c.Code == code)?.Name;
    }
}
