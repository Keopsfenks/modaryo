namespace ERP.Domain.Pricing;

/// <summary>Kademeli fiyat: MinQty ve üzeri miktarlarda geçerli birim fiyat.</summary>
public sealed record PriceTier(decimal MinQty, decimal Price);
