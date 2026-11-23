namespace ERP.Domain.Pricing;

public enum PriceListKind { Sale, Purchase }

public enum PriceSource { CustomerPrice, GroupList, DefaultList, SupplierList, CardDefault, None }

public enum RoundingRule { None, Cent, FiveCents, TenCents, FiftyCents, One, NineEnding }

public enum CampaignKind { BuyXPayY, PercentOff }
