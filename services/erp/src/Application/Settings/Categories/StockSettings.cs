using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory;
using ERP.Domain.Inventory;

namespace ERP.Application.Settings.Categories;

/// <summary>
///     Stok ayarları: maliyet yöntemi ve eksi stok. Maliyet yöntemi yalnızca firmanın hiç stok hareketi yokken
///     değiştirilebilir (bkz. <see cref="StockSettingsRules.EnsureMethodUnlockedAsync" />).
/// </summary>
public sealed record StockSettings(
    CostingMethod Method        = CostingMethod.WeightedAverage,
    bool          AllowNegative = false
) : ISettingsCategory<StockSettings> {
    public StockSettings() : this(CostingMethod.WeightedAverage) { }

    public static string Key => "stok";

    public static string Permission => InventoryPermissions.Settings;
}

public sealed class StockSettingsValidator : AbstractValidator<StockSettings> {
    public StockSettingsValidator() {
        RuleFor(s => s.Method).IsInEnum().WithMessage("Maliyet yöntemi geçersiz.");
    }
}

public static class StockSettingsRules {
    /// <summary>Yöntem değişiyorsa ve firmada hareket varsa 409 costing_method_locked.</summary>
    public static async Task EnsureMethodUnlockedAsync(IApplicationDbContext db,
                                                       StockSettings         current,
                                                       StockSettings         next,
                                                       CancellationToken     ct
    ) {
        if (current.Method != next.Method && await db.StockMovements.AnyAsync(ct))
            throw InventoryErrors.CostingMethodLocked();
    }
}
