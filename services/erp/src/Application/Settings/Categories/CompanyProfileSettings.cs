using ERP.Application.Common.Security;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.Settings.Categories;

public sealed record IbanEntry(string BankName, string Iban, string Currency);

public sealed record CompanyProfileSettings(
    string?     LegalName,
    string?     TaxNumber,
    string?     TaxOffice,
    string?     MersisNo,
    string?     TradeRegistryNo,
    string?     Address,
    string?     City,
    string?     District,
    string?     Phone,
    string?     Email,
    string?     Website,
    Guid?       LogoFileId,
    IbanEntry[] Ibans
) : ISettingsCategory<CompanyProfileSettings> {
    public CompanyProfileSettings() : this(null, null, null, null, null, null, null, null, null, null, null, null, []) { }

    public static string Key => "firma";

    public static string Permission => SettingsPermissions.Company;

    /// <summary>Boşlukları kırpar, boş metinleri null yapar; IBAN'ı büyük harfe çevirip boşluklarını siler.</summary>
    public static CompanyProfileSettings Normalize(CompanyProfileSettings s) {
        return s with {
                          LegalName       = Clean(s.LegalName),
                          TaxNumber       = Clean(s.TaxNumber),
                          TaxOffice       = Clean(s.TaxOffice),
                          MersisNo        = Clean(s.MersisNo),
                          TradeRegistryNo = Clean(s.TradeRegistryNo),
                          Address         = Clean(s.Address),
                          City            = Clean(s.City),
                          District        = Clean(s.District),
                          Phone           = Clean(s.Phone),
                          Email           = Clean(s.Email),
                          Website         = Clean(s.Website),
                          Ibans = (s.Ibans ?? [])
                              .Where(i => i is not null)
                              .Select(i => new IbanEntry((i.BankName ?? string.Empty).Trim(),
                                                         NormalizeIban(i.Iban),
                                                         (i.Currency ?? string.Empty).Trim().ToUpperInvariant()))
                              .ToArray()
                      };
    }

    public static string NormalizeIban(string? iban) {
        return string.Concat((iban ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    }

    /// <summary>TR + 24 rakam ve mod-97 sağlama toplamı (girdi normalize edilmiş olmalı).</summary>
    public static bool IsValidIban(string iban) {
        if (iban.Length != 26 || !iban.StartsWith("TR", StringComparison.Ordinal)) return false;
        if (!iban.Skip(2).All(c => c is >= '0' and <= '9')) return false;

        var rearranged = iban[4..] + "2927" + iban[2..4]; // T=29, R=27
        var remainder  = 0;
        foreach (var c in rearranged) remainder = (remainder * 10 + (c - '0')) % 97;

        return remainder == 1;
    }

    private static string? Clean(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed class CompanyProfileSettingsValidator : AbstractValidator<CompanyProfileSettings> {
    public CompanyProfileSettingsValidator() {
        RuleFor(s => s.LegalName).MaximumLength(200).WithMessage("Unvan en fazla 200 karakter olabilir.");
        RuleFor(s => s.TaxNumber)
            .Must(BeValidTaxNumber).When(s => s.TaxNumber is not null)
            .WithMessage("Vergi numarası geçerli bir VKN (10 hane) veya TCKN (11 hane) olmalıdır.");
        RuleFor(s => s.TaxOffice).MaximumLength(100).WithMessage("Vergi dairesi en fazla 100 karakter olabilir.");
        RuleFor(s => s.MersisNo).MaximumLength(50).WithMessage("Mersis numarası en fazla 50 karakter olabilir.");
        RuleFor(s => s.TradeRegistryNo).MaximumLength(50)
            .WithMessage("Ticaret sicil numarası en fazla 50 karakter olabilir.");
        RuleFor(s => s.Address).MaximumLength(500).WithMessage("Adres en fazla 500 karakter olabilir.");
        RuleFor(s => s.City).MaximumLength(100).WithMessage("İl en fazla 100 karakter olabilir.");
        RuleFor(s => s.District).MaximumLength(100).WithMessage("İlçe en fazla 100 karakter olabilir.");
        RuleFor(s => s.Phone).MaximumLength(50).WithMessage("Telefon en fazla 50 karakter olabilir.");
        RuleFor(s => s.Email).EmailAddress().MaximumLength(200).When(s => s.Email is not null)
            .WithMessage("E-posta adresi geçerli değil.");
        RuleFor(s => s.Website).MaximumLength(200).WithMessage("Web sitesi en fazla 200 karakter olabilir.");
        RuleFor(s => s.Ibans).Must(i => i.Length <= 20).WithMessage("En fazla 20 banka hesabı eklenebilir.");
        RuleForEach(s => s.Ibans).ChildRules(iban => {
                                                 iban.RuleFor(i => i.BankName).NotEmpty().MaximumLength(100)
                                                     .WithMessage("Banka adı zorunludur ve en fazla 100 karakter olabilir.");
                                                 iban.RuleFor(i => i.Iban).Must(CompanyProfileSettings.IsValidIban)
                                                     .WithMessage("IBAN geçerli değil (TR ile başlayan 26 karakter ve sağlama toplamı doğru olmalı).");
                                                 iban.RuleFor(i => i.Currency)
                                                     .Must(c => GeneralSettings.Currencies.Contains(c))
                                                     .WithMessage("Para birimi TRY, USD, EUR veya GBP olmalıdır.");
                                             });
    }

    private static bool BeValidTaxNumber(string? value) {
        return value?.Length switch {
                   10 => TaxNumber.IsValidVkn(value),
                   11 => TaxNumber.IsValidTckn(value),
                   _  => false
               };
    }
}
