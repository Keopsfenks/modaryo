using ERP.Application.Common.Security;

namespace ERP.Application.Settings.Categories;

/// <summary>Kurulum sihirbazı durumu: tamamlanma, ertelenme zamanı ve seçilen sektör.</summary>
public sealed record SetupSettings(
    DateTimeOffset? CompletedAt = null,
    DateTimeOffset? DismissedAt = null,
    string?         Sector      = null
) : ISettingsCategory<SetupSettings> {
    public SetupSettings() : this(null, null, null) { }

    public static string Key => "kurulum";

    public static string Permission => SettingsPermissions.Company;
}

public sealed class SetupSettingsValidator : AbstractValidator<SetupSettings> {
    public SetupSettingsValidator() {
        RuleFor(s => s.Sector).MaximumLength(50).WithMessage("Sektör en fazla 50 karakter olabilir.");
    }
}
