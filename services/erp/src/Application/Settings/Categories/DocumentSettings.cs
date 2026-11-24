using System.Text.RegularExpressions;
using ERP.Application.Invoicing;

namespace ERP.Application.Settings.Categories;

/// <summary>Belge (fatura PDF'i ve e-posta) görünümü.</summary>
public sealed record DocumentSettings(
    string  AccentColor         = "#5D87FF",
    string? FooterNote          = null,
    bool    ShowIbans           = true,
    string  InvoiceEmailSubject = "{firma} - {belgeNo} numaralı fatura",
    string  InvoiceEmailBody    = "Sayın {alici}, {belgeNo} numaralı faturanız ektedir."
) : ISettingsCategory<DocumentSettings> {
    public DocumentSettings() : this("#5D87FF") { }

    public static string Key => "belgeler";

    public static string Permission => InvoicingPermissions.SettingsDocuments;
}

public sealed partial class DocumentSettingsValidator : AbstractValidator<DocumentSettings> {
    public DocumentSettingsValidator() {
        RuleFor(s => s.AccentColor).Must(c => c is not null && HexColor().IsMatch(c))
            .WithMessage("Vurgu rengi #RRGGBB biçiminde olmalıdır.");
        RuleFor(s => s.FooterNote).MaximumLength(500).WithMessage("Alt not en fazla 500 karakter olabilir.");
        RuleFor(s => s.InvoiceEmailSubject).NotEmpty().WithMessage("E-posta konusu zorunludur.")
            .MaximumLength(200).WithMessage("E-posta konusu en fazla 200 karakter olabilir.");
        RuleFor(s => s.InvoiceEmailBody).NotEmpty().WithMessage("E-posta metni zorunludur.")
            .MaximumLength(2000).WithMessage("E-posta metni en fazla 2000 karakter olabilir.");
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
