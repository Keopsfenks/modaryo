using ERP.Domain.Pos;

namespace ERP.Application.Pos.Sales;

/// <summary>
///     Satış satırı girdisi. Birim fiyat sunucuda çözülür (P2 satış önceliği); istemci UnitPrice'ı yalnızca kalemin fiyatı yoksa (açık fiyatlı kalem)
///     kullanılır. FixedTotal terazi barkodunun fiyat tipinde barkoddaki (KDV dahil) tutardır: satır toplamı buna eşitlenir; sunucu miktar x fiyatla
///     tutarlılığını yeniden doğrular ve indirim/kampanya uygulamaz.
/// </summary>
public sealed record SaleLineInput(
    Guid     ProductId,
    Guid?    VariantId,
    string?  Unit,
    decimal  Quantity,
    decimal? UnitPrice,
    decimal? ManualDiscountPercent,
    decimal? FixedTotal
);

public sealed record SalePaymentInput(PosPaymentMethod Method, decimal Amount);

/// <summary>ClientSaleId istemcinin ürettiği tek seferlik kimliktir: bağlantısı kopan istemci aynı satışı güvenle yeniden gönderebilir.</summary>
public sealed record SaleInput(
    Guid                       ClientSaleId,
    Guid                       RegisterId,
    Guid?                      CustomerId,
    IReadOnlyList<SaleLineInput> Lines,
    IReadOnlyList<SalePaymentInput> Payments
);

public sealed record SlipLineDto(string Description, decimal Quantity, string Unit, decimal UnitPrice, decimal Discount, decimal CampaignDiscount, int VatRate, decimal Total);

public sealed record SlipVatDto(int Rate, decimal Base, decimal Vat);

public sealed record SlipPaymentDto(PosPaymentMethod Method, decimal Amount);

/// <summary>
///     80 mm fiş yazıcısı için yapısal model. Tutarlar KDV dahildir (birim fiyat ve indirimler KDV dahil gösterilir). Notes yasal belge moduna
///     göre sabit metinleri taşır; Barcode fiş numarasıdır (Code128).
/// </summary>
public sealed record SlipDto(
    string                       Firm,
    string                       Branch,
    string                       Register,
    string                       Cashier,
    string                       Number,
    DateTimeOffset               At,
    LegalDocumentMode            Mode,
    IReadOnlyList<SlipLineDto>   Lines,
    IReadOnlyList<SlipVatDto>    VatSummary,
    decimal                      Subtotal,
    decimal                      Discount,
    decimal                      Total,
    IReadOnlyList<SlipPaymentDto> Payments,
    decimal                      Change,
    string?                      InvoiceNumber,
    string                       Barcode,
    IReadOnlyList<string>        Notes
);

/// <summary>Satış sonucu. Replayed: aynı ClientSaleId ile gelen yinelenen gönderim; kayıtlı satış (aynı id, numara ve fiş) döner.</summary>
public sealed record SaleResultDto(
    Guid     Id,
    string   Number,
    decimal  Total,
    decimal  Change,
    string?  InvoiceNumber,
    bool     Replayed,
    SlipDto  Slip
);

public sealed record SaleLineDto(
    Guid Id, int Sort, Guid ProductId, Guid? VariantId, string Description, string Unit, decimal Quantity, decimal UnitPrice, int VatRate,
    decimal Discount, decimal CampaignDiscount, decimal Net, decimal Vat, decimal Total, decimal ReturnedQuantity
);

public sealed record SaleDto(
    Guid                         Id,
    string                       Number,
    PosSaleStatus                Status,
    Guid                         RegisterId,
    Guid                         ShiftId,
    Guid                         CurrentAccountId,
    DateOnly                     Date,
    DateTimeOffset               At,
    LegalDocumentMode            Mode,
    decimal                      Subtotal,
    decimal                      Discount,
    decimal                      VatTotal,
    decimal                      Total,
    decimal                      Change,
    Guid?                        InvoiceId,
    string?                      InvoiceNumber,
    string?                      CancelReason,
    IReadOnlyList<SaleLineDto>   Lines,
    IReadOnlyList<SlipPaymentDto> Payments,
    SlipDto                      Slip
);
