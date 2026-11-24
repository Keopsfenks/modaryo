using ERP.Domain.Pricing;

namespace ERP.Application.Pricing.Quotes;

/// <summary>
///     Fiyat sorgusu. Kind=Sale için CurrentAccountId müşteri, Purchase için tedarikçidir (boş olabilir).
///     Tarih, fiyat listesi ve müşteri fiyatlarının geçerlilik tarihidir.
/// </summary>
public sealed record QuoteRequest(
    PriceListKind Kind,
    Guid?         CurrentAccountId,
    Guid          ProductId,
    Guid?         VariantId,
    string        Unit,
    decimal       Quantity,
    DateOnly      Date
);

public interface IPriceQuoteService {
    Task<PriceQuote?> QuoteAsync(QuoteRequest request, CancellationToken ct);

    /// <summary>Sonuçlar istek sırasındadır; fiyat bulunamayan istek null döner. Toplu çağrı en çok 2 veritabanı sorgusu yapar.</summary>
    Task<IReadOnlyList<PriceQuote?>> QuoteManyAsync(IReadOnlyList<QuoteRequest> requests, CancellationToken ct);
}
