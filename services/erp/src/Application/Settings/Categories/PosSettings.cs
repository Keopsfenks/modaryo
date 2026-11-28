using System.Text.Json.Serialization;
using ERP.Application.Pos;
using ERP.Domain.Pos;

namespace ERP.Application.Settings.Categories;

/// <summary>Terazi barkod biçimi (API/ayar biçimi): önek + kalem kodu + değer + kontrol hanesi, toplam 13 hane.</summary>
public sealed record ScaleBarcodeSetting(string Prefix, int ItemCodeLength, int ValueLength, ScaleValueKind ValueKind) {
    public ScaleFormat ToDomain() {
        return new ScaleFormat(Prefix, ItemCodeLength, ValueLength, ValueKind);
    }
}

/// <summary>
///     POS ayarları. RetailCustomerId sistemce yönetilir (ilk erişimde "Perakende Müşteri" carisi açılır); istemciden gelen değer yok sayılır.
///     RequireCustomerForVeresiye her zaman true'dur.
/// </summary>
public sealed record PosSettings(
    LegalDocumentMode        LegalDocumentMode             = LegalDocumentMode.FiscalReceipt,
    Guid?                    RetailCustomerId              = null,
    ScaleBarcodeSetting[]?   ScaleBarcodes                 = null,
    bool                     AllowNegativeCashWarningOnly  = true,
    bool                     RequireCustomerForVeresiye    = true
) : ISettingsCategory<PosSettings> {
    public PosSettings() : this(LegalDocumentMode.FiscalReceipt) { }

    [JsonIgnore]
    public ScaleBarcodeSetting[] Scales => ScaleBarcodes ?? [];

    [JsonIgnore]
    public IReadOnlyList<ScaleFormat> ScaleFormats => Scales.Select(s => s.ToDomain()).ToList();

    public static PosSettings Normalize(PosSettings s) {
        return s with { ScaleBarcodes = s.Scales, RequireCustomerForVeresiye = true };
    }

    public static string Key => "pos";

    public static string Permission => PosPermissions.SettingsPos;
}

public sealed class PosSettingsValidator : AbstractValidator<PosSettings> {
    public PosSettingsValidator() {
        RuleFor(s => s.LegalDocumentMode).IsInEnum().WithMessage("Yasal belge modu geçersiz.");
        RuleFor(s => s.ScaleBarcodes).Must(c => c is null || c.Select(x => x.Prefix).Distinct(StringComparer.Ordinal).Count() == c.Length)
                                     .WithMessage("Terazi barkodu önekleri benzersiz olmalıdır.");
        RuleForEach(s => s.ScaleBarcodes).ChildRules(c => {
            c.RuleFor(x => x.Prefix).Must(p => p is { Length: >= 1 and <= 6 } && p.All(char.IsAsciiDigit))
             .WithMessage("Terazi barkodu öneki rakamlardan oluşmalıdır (en çok 6 hane).");
            c.RuleFor(x => x.ItemCodeLength).InclusiveBetween(1, 11).WithMessage("Kalem kodu uzunluğu 1 ile 11 arasında olmalıdır.");
            c.RuleFor(x => x.ValueLength).InclusiveBetween(1, 11).WithMessage("Değer uzunluğu 1 ile 11 arasında olmalıdır.");
            c.RuleFor(x => x.ValueKind).IsInEnum().WithMessage("Değer türü geçersiz.");
            c.RuleFor(x => x).Must(x => x.Prefix is not null && x.Prefix.Length + x.ItemCodeLength + x.ValueLength + 1 == ScaleBarcode.Ean13Length)
             .WithMessage("Önek, kalem kodu ve değer uzunlukları toplamı kontrol hanesiyle birlikte 13 olmalıdır.");
        });
    }
}
