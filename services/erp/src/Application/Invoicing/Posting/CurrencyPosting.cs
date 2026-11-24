using ERP.Application.Common.Interfaces;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Posting;

/// <summary>Defter tutarı (cari para biriminde) ve belge para biriminden çevrildiyse özgün para birimi, tutar ve kur.</summary>
public sealed record PostedAmount(decimal Amount, string Currency, string? SourceCurrency, decimal? SourceAmount, decimal? Rate);

/// <summary>
///     Belge tutarının cari para birimine çevrilmesi (<see cref="InvoiceRules.LedgerAmountAsync" /> ile aynı kural):
///     aynı para birimi = belge tutarı; TL cari = TL karşılığı; TL belge ve yabancı cari = TL / carinin aynı gün kuru;
///     farklı iki yabancı para = TL üzerinden. Rate: çevirmede kullanılan kur (yabancı belge ve TL cari için belge kuru,
///     yabancı cari için carinin para biriminin aynı gün kuru).
/// </summary>
public sealed class CurrencyPosting(IExchangeRates rates) {
    public async Task<PostedAmount> ConvertAsync(Invoice invoice, string cariCurrency, CancellationToken ct) {
        if (cariCurrency == invoice.Currency) return new PostedAmount(invoice.Payable, cariCurrency, null, null, null);
        if (cariCurrency == "TRY") return new PostedAmount(invoice.PayableTl, cariCurrency, invoice.Currency, invoice.Payable, invoice.Rate);

        var (cariRate, _) = await rates.GetAsync(cariCurrency, invoice.Date, invoice.RateType, ct);
        return new PostedAmount(InvoiceRules.Round(invoice.PayableTl / cariRate), cariCurrency, invoice.Currency, invoice.Payable, cariRate);
    }
}
