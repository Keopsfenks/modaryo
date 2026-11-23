using ERP.Application.Common.Security;

namespace ERP.Application.Settings.Categories;

public sealed record GeneralSettings(
    string      DefaultCurrency,
    int         QuantityDecimals,
    int         UnitPriceDecimals,
    DayOfWeek   WeekStart,
    DayOfWeek[] WorkDays,
    DateOnly[]  Holidays,
    string      DateFormat = "dd.MM.yyyy"
) : ISettingsCategory<GeneralSettings> {
    public static readonly string[] Currencies = ["TRY", "USD", "EUR", "GBP"];

    public static readonly string[] DateFormats = ["dd.MM.yyyy", "yyyy-MM-dd", "dd/MM/yyyy"];

    public GeneralSettings() : this("TRY", 2, 2, DayOfWeek.Monday,
                                    [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                                     DayOfWeek.Friday], [], "dd.MM.yyyy") { }

    public static string Key => "genel";

    public static string Permission => SettingsPermissions.General;

    /// <summary>Çalışma günlerini ve tatilleri tekilleştirip sıralar.</summary>
    public static GeneralSettings Normalize(GeneralSettings s) {
        return s with {
                          DefaultCurrency = (s.DefaultCurrency ?? string.Empty).Trim().ToUpperInvariant(),
                          WorkDays        = (s.WorkDays ?? []).Distinct().Order().ToArray(),
                          Holidays        = (s.Holidays ?? []).Distinct().Order().ToArray()
                      };
    }
}

public sealed class GeneralSettingsValidator : AbstractValidator<GeneralSettings> {
    public GeneralSettingsValidator() {
        RuleFor(s => s.DefaultCurrency).Must(c => GeneralSettings.Currencies.Contains(c))
            .WithMessage("Varsayılan para birimi TRY, USD, EUR veya GBP olmalıdır.");
        RuleFor(s => s.QuantityDecimals).InclusiveBetween(0, 4)
            .WithMessage("Miktar ondalık basamağı 0 ile 4 arasında olmalıdır.");
        RuleFor(s => s.UnitPriceDecimals).InclusiveBetween(2, 6)
            .WithMessage("Birim fiyat ondalık basamağı 2 ile 6 arasında olmalıdır.");
        RuleFor(s => s.WeekStart).IsInEnum().WithMessage("Haftanın ilk günü geçerli değil.");
        RuleFor(s => s.WorkDays).NotEmpty().WithMessage("En az bir çalışma günü seçilmelidir.");
        RuleForEach(s => s.WorkDays).IsInEnum().WithMessage("Çalışma günü geçerli değil.");
        RuleFor(s => s.DateFormat).Must(f => GeneralSettings.DateFormats.Contains(f))
            .WithMessage("Tarih biçimi dd.MM.yyyy, yyyy-MM-dd veya dd/MM/yyyy olmalıdır.");
        RuleFor(s => s.Holidays).Must(h => h.Length <= 1000).WithMessage("En fazla 1000 tatil günü tanımlanabilir.");
    }
}
