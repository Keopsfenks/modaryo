using ERP.Application.Pricing;
using ERP.Domain.Pricing;

namespace ERP.Application.Settings.Categories;

/// <summary>Fiyatlandırma ayarları: varsayılan satış/alış listesi, KDV gösterimi ve türetilmiş fiyat yuvarlaması.</summary>
public sealed record PricingSettings(
    Guid?        DefaultSaleListId,
    Guid?        DefaultPurchaseListId,
    bool         ShowVatIncluded = true,
    RoundingRule Rounding        = RoundingRule.None
) : ISettingsCategory<PricingSettings> {
    public PricingSettings() : this(null, null) { }

    public static string Key => "fiyat";

    public static string Permission => PricingPermissions.Settings;
}

public sealed class PricingSettingsValidator : AbstractValidator<PricingSettings> {
    public PricingSettingsValidator() {
        RuleFor(s => s.Rounding).IsInEnum().WithMessage("Yuvarlama kuralı geçersiz.");
    }
}
