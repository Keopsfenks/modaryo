using ERP.Application.Sales;

namespace ERP.Application.Settings.Categories;

/// <summary>Satış ayarları: teklif geçerlilik süresi, satış temsilcisi zorunluluğu, varsayılan taksit sayısı, ön ödeme varsayılanı.</summary>
public sealed record SalesSettings(
    int  QuoteValidityDays       = 15,
    bool RequireSalesRep         = false,
    int  DefaultInstallmentCount = 3,
    bool PrepaymentDefaultOff    = true
) : ISettingsCategory<SalesSettings> {
    public SalesSettings() : this(15) { }

    public static string Key => "satis";

    public static string Permission => SalesPermissions.Settings;
}

public sealed class SalesSettingsValidator : AbstractValidator<SalesSettings> {
    public SalesSettingsValidator() {
        RuleFor(s => s.QuoteValidityDays).InclusiveBetween(1, 365).WithMessage("Teklif geçerlilik süresi 1 ile 365 gün arasında olmalıdır.");
        RuleFor(s => s.DefaultInstallmentCount).InclusiveBetween(1, 36).WithMessage("Varsayılan taksit sayısı 1 ile 36 arasında olmalıdır.");
    }
}
