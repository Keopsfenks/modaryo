namespace ERP.Domain.Invoicing;

/// <summary>Kesim anında dondurulan alıcı/satıcı bilgisi.</summary>
public sealed record AddressSnapshot(
    string  Title,
    string? TaxNumber,
    string? TaxOffice,
    string? Address,
    string? City,
    string? District,
    string? Country,
    string? Email);

/// <summary>Parti/lot girişi (stok etkili satırlar için).</summary>
public sealed record InvoiceLot(string LotNo, DateOnly? Expiry, decimal Quantity, Guid? LotId);
