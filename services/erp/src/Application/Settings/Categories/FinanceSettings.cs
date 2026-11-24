using ERP.Application.Common.Security;
using ERP.Domain.Invoicing;

namespace ERP.Application.Settings.Categories;

/// <summary>PeriodLockDate (dahil) ve öncesindeki tarihli kayıtlar kilitlidir.</summary>
public sealed record FinanceSettings(
    DateOnly? PeriodLockDate,
    RateType  RateType = RateType.ForexBuying
) : ISettingsCategory<FinanceSettings> {
    public FinanceSettings() : this((DateOnly?)null) { }

    public static string Key => "finans";

    public static string Permission => SettingsPermissions.Finance;
}

public sealed class FinanceSettingsValidator : AbstractValidator<FinanceSettings> {
    public FinanceSettingsValidator() {
        RuleFor(s => s.RateType).IsInEnum().WithMessage("Kur türü geçersiz.");
    }
}
