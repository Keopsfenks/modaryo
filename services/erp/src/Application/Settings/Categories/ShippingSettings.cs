using ERP.Application.Shipping;

namespace ERP.Application.Settings.Categories;

/// <summary>Kargo firması: kod, ad, takip bağlantı şablonu ({no} takip numarasıyla değişir) ve etkinlik.</summary>
public sealed record CarrierInfo(string Code, string Name, string? TrackingUrlTemplate, bool IsActive);

/// <summary>
///     Sevkiyat ayarları: toplama-paketleme kullanımı, müşteri bildirimi, kargo listesi ve bildirim e-postası şablonu.
///     Şablon değişkenleri: {alici}, {belgeNo}, {kargo}, {takipNo}, {takipLink}.
/// </summary>
public sealed record ShippingSettings(
    bool           UsePickPack          = true,
    bool           NotifyCustomerOnShip = true,
    CarrierInfo[]? Carriers             = null,
    string         ShipEmailSubject     = ShippingSettings.DefaultSubject,
    string         ShipEmailBody        = ShippingSettings.DefaultBody
) : ISettingsCategory<ShippingSettings> {
    public const string DefaultSubject = "Siparişiniz kargoya verildi ({belgeNo})";

    public const string DefaultBody =
        "Sayın {alici},\n\n{belgeNo} numaralı sevkiyatınız {kargo} ile yola çıktı.\nTakip numarası: {takipNo}\nTakip bağlantısı: {takipLink}\n\nİyi günler dileriz.";

    /// <summary>Kendi aracımız: teslimat planıyla teslim edilir, takip numarası istenmez.</summary>
    public const string OwnVehicleCode = "kendi-arac";

    /// <summary>
    ///     Takip bağlantı şablonları (2026-11-28 tarihinde sabitlendi). Her firmanın herkese açık gönderi sorgulama sayfasının
    ///     takip numarası sorgu parametresi biçimidir; firmalar sayfa yapısını değiştirebileceğinden ayarlardan düzenlenebilir.
    /// </summary>
    public static readonly CarrierInfo[] CarrierSeed = [
        new("yurtici", "Yurtiçi", "https://www.yurticikargo.com/tr/online-servisler/gonderi-sorgula?code={no}", true),
        new("aras", "Aras", "https://kargotakip.araskargo.com.tr/mainpage.aspx?code={no}", true),
        new("mng", "MNG", "https://www.mngkargo.com.tr/gonderi-takip?code={no}", true),
        new("ptt", "PTT Kargo", "https://gonderitakip.ptt.gov.tr/Track/Verify?q={no}", true),
        new("surat", "Sürat", "https://www.suratkargo.com.tr/kargo-takip?kargotakipno={no}", true),
        new("hepsijet", "HepsiJet", "https://www.hepsijet.com/gonderi-takibi/{no}", true),
        new("kolaygelsin", "Kolay Gelsin", "https://www.kolaygelsin.com/gonderi-takip?code={no}", true),
        new("diger", "Diğer", null, true),
        new(OwnVehicleCode, "Kendi aracımız", null, true)
    ];

    public ShippingSettings() : this(true, true, CarrierSeed) { }

    public static ShippingSettings Normalize(ShippingSettings s) {
        return s with {
            Carriers = s.Carriers is { Length: > 0 } ? s.Carriers : CarrierSeed,
            ShipEmailSubject = string.IsNullOrWhiteSpace(s.ShipEmailSubject) ? DefaultSubject : s.ShipEmailSubject.Trim(),
            ShipEmailBody = string.IsNullOrWhiteSpace(s.ShipEmailBody) ? DefaultBody : s.ShipEmailBody
        };
    }

    public static string Key => "sevkiyat";

    public static string Permission => ShippingPermissions.Settings;
}

public sealed class ShippingSettingsValidator : AbstractValidator<ShippingSettings> {
    public ShippingSettingsValidator() {
        RuleFor(s => s.ShipEmailSubject).MaximumLength(200).WithMessage("E-posta konusu en fazla 200 karakter olabilir.");
        RuleFor(s => s.ShipEmailBody).MaximumLength(4000).WithMessage("E-posta metni en fazla 4000 karakter olabilir.");
        RuleFor(s => s.Carriers).Must(c => c is null || c.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() == c.Length)
                                .WithMessage("Kargo firması kodları benzersiz olmalıdır.");
        RuleForEach(s => s.Carriers).ChildRules(c => {
            c.RuleFor(x => x.Code).NotEmpty().MaximumLength(30).WithMessage("Kargo firması kodu 1 ile 30 karakter arasında olmalıdır.");
            c.RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("Kargo firması adı 1 ile 100 karakter arasında olmalıdır.");
            c.RuleFor(x => x.TrackingUrlTemplate)
             .Must(t => string.IsNullOrWhiteSpace(t) || (t.StartsWith("https://", StringComparison.Ordinal) && t.Contains("{no}", StringComparison.Ordinal) && t.Length <= 500))
             .WithMessage("Takip bağlantısı https ile başlamalı ve {no} içermelidir.");
        });
    }
}
