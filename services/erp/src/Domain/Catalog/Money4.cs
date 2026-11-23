namespace ERP.Domain.Catalog;

/// <summary>Birim fiyat (numeric(18,4)), para birimi ve KDV dahil bilgisi.</summary>
public sealed record Money4(decimal Amount, string Currency, bool VatIncluded);
