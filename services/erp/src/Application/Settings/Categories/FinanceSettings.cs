using ERP.Application.Common.Security;

namespace ERP.Application.Settings.Categories;

/// <summary>PeriodLockDate (dahil) ve öncesindeki tarihli kayıtlar kilitlidir.</summary>
public sealed record FinanceSettings(
    DateOnly? PeriodLockDate
) : ISettingsCategory<FinanceSettings> {
    public FinanceSettings() : this((DateOnly?)null) { }

    public static string Key => "finans";

    public static string Permission => SettingsPermissions.Finance;
}

public sealed class FinanceSettingsValidator : AbstractValidator<FinanceSettings> { }
