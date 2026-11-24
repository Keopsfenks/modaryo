using ERP.Application.Invoicing;
using ERP.Domain.Invoicing;

namespace ERP.Application.Settings.Categories;

/// <summary>e-Belge (e-Fatura / e-Arşiv / e-İrsaliye) kullanımı. Açıkken kesilen faturalar e-belge modunda numaralanır.</summary>
public sealed record EBelgeSettings(
    bool             Enabled          = false,
    EInvoiceScenario DefaultScenario  = EInvoiceScenario.Basic,
    bool             EIrsaliyeEnabled = false
) : ISettingsCategory<EBelgeSettings> {
    public EBelgeSettings() : this(false) { }

    public static string Key => "ebelge";

    public static string Permission => InvoicingPermissions.SettingsEBelge;
}

public sealed class EBelgeSettingsValidator : AbstractValidator<EBelgeSettings> {
    public EBelgeSettingsValidator() {
        RuleFor(s => s.DefaultScenario).IsInEnum().WithMessage("e-Fatura senaryosu geçersiz.");
    }
}
