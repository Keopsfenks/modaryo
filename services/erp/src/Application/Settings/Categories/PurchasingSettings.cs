using ERP.Application.Purchasing;
using ERP.Domain.Purchasing;

namespace ERP.Application.Settings.Categories;

/// <summary>
///     Satın alma ayarları: fazla teslimat toleransı, fiyat farkı eşiği, karantina ve masraf dağıtımı varsayılan anahtarı.
///     QuarantineWarehouseByBranch: şube kimliği -> o şubenin karantina deposu.
/// </summary>
public sealed record PurchasingSettings(
    decimal                  OverDeliveryPercent         = 0m,
    decimal                  PriceVariancePercent        = 2m,
    bool                     QuarantineEnabled           = false,
    Dictionary<Guid, Guid>?  QuarantineWarehouseByBranch = null,
    AllocationKey            DefaultLandedKey            = AllocationKey.Amount
) : ISettingsCategory<PurchasingSettings> {
    public PurchasingSettings() : this(0m, 2m, false, new Dictionary<Guid, Guid>()) { }

    public static PurchasingSettings Normalize(PurchasingSettings s) {
        return s with { QuarantineWarehouseByBranch = s.QuarantineWarehouseByBranch ?? new Dictionary<Guid, Guid>() };
    }

    public static string Key => "satinalma";

    public static string Permission => PurchasingPermissions.Settings;
}

public sealed class PurchasingSettingsValidator : AbstractValidator<PurchasingSettings> {
    public PurchasingSettingsValidator() {
        RuleFor(s => s.OverDeliveryPercent).InclusiveBetween(0m, 100m).WithMessage("Fazla teslimat toleransı 0 ile 100 arasında olmalıdır.");
        RuleFor(s => s.PriceVariancePercent).InclusiveBetween(0m, 100m).WithMessage("Fiyat farkı eşiği 0 ile 100 arasında olmalıdır.");
        RuleFor(s => s.DefaultLandedKey).IsInEnum().WithMessage("Dağıtım anahtarı geçersiz.");
    }
}
