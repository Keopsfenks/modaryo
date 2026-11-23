using System.Text.RegularExpressions;
using ERP.Application.Catalog;

namespace ERP.Application.Settings.Categories;

/// <summary>Katalog ayarları: kod şablonu, KDV, birim listesi, barkod öneki ve sıra sayaçları.</summary>
public sealed record CatalogSettings(
    string   CodeTemplate,
    int      DefaultVatRate,
    string[] Units,
    string   BarcodePrefix,
    int      NextBarcodeSequence,
    int      NextCodeSequence
) : ISettingsCategory<CatalogSettings> {
    public static readonly int[] VatRates = [0, 1, 10, 20];

    public static readonly string[] DefaultUnits =
        ["adet", "kg", "g", "lt", "m", "m2", "m3", "koli", "paket", "saat", "gün", "iş"];

    public CatalogSettings() : this("U{00000}", 20, DefaultUnits, "2000000", 1, 1) { }

    public static string Key => "katalog";

    public static string Permission => CatalogPermissions.Settings;

    /// <summary>Birimleri kırpıp tekilleştirir; şablon ve önekteki boşlukları siler.</summary>
    public static CatalogSettings Normalize(CatalogSettings s) {
        return s with {
                          CodeTemplate  = (s.CodeTemplate ?? string.Empty).Trim(),
                          BarcodePrefix = (s.BarcodePrefix ?? string.Empty).Trim(),
                          Units = (s.Units ?? [])
                              .Where(u => !string.IsNullOrWhiteSpace(u))
                              .Select(u => u.Trim().ToLowerInvariant())
                              .Distinct().ToArray()
                      };
    }
}

public sealed partial class CatalogSettingsValidator : AbstractValidator<CatalogSettings> {
    public CatalogSettingsValidator() {
        RuleFor(s => s.CodeTemplate).Must(t => TemplateRegex().IsMatch(t))
            .WithMessage("Ürün kodu şablonu bir önek ve {00000} biçiminde sıra alanı içermelidir (örn. U{00000}).");
        RuleFor(s => s.DefaultVatRate).Must(v => CatalogSettings.VatRates.Contains(v))
            .WithMessage("KDV oranı 0, 1, 10 veya 20 olmalıdır.");
        RuleFor(s => s.Units).NotEmpty().WithMessage("En az bir birim tanımlanmalıdır.")
            .Must(u => u.Length <= 50).WithMessage("En fazla 50 birim tanımlanabilir.");
        RuleForEach(s => s.Units).Must(u => u.Length is >= 1 and <= 20)
            .WithMessage("Birim adı 1 ile 20 karakter arasında olmalıdır.");
        RuleFor(s => s.BarcodePrefix).Must(p => PrefixRegex().IsMatch(p))
            .WithMessage("Barkod öneki 7 haneli sayı olmalıdır.");
        RuleFor(s => s.NextBarcodeSequence).InclusiveBetween(1, 99999)
            .WithMessage("Sonraki barkod sırası 1 ile 99999 arasında olmalıdır.");
        RuleFor(s => s.NextCodeSequence).InclusiveBetween(1, 999_999_999)
            .WithMessage("Sonraki ürün kodu sırası geçersiz.");
    }

    [GeneratedRegex(@"^[^{}\s]{0,15}\{0{1,9}\}[^{}\s]{0,5}$")]
    private static partial Regex TemplateRegex();

    [GeneratedRegex(@"^\d{7}$")]
    private static partial Regex PrefixRegex();
}
