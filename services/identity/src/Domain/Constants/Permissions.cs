namespace IdentityService.Domain.Constants;

/// <summary>
/// İzin adları "modül.eylem" biçimindedir. Kod rol adına değil izne bakar.
/// </summary>
public static class Permissions
{
    /// <summary>Tüm izinleri karşılar (Sahip rolü).</summary>
    public const string All = "*";

    public const string CompanyUpdate = "company.update";
    public const string MembersRead = "members.read";
    public const string MembersInvite = "members.invite";
    public const string MembersManage = "members.manage";

    public const string CariRead = "cari.read";
    public const string CariManage = "cari.manage";
    public const string CariMovements = "cari.movements";

    public const string RolesRead = "roller.read";
    public const string RolesManage = "roller.manage";

    public const string SettingsModules = "ayarlar.moduller";
    public const string SettingsBranches = "ayarlar.subeler";
    public const string SettingsCompany = "ayarlar.firma";
    public const string SettingsGeneral = "ayarlar.genel";
    public const string SettingsFinance = "ayarlar.finans";

    public const string SettingsCatalog = "ayarlar.katalog";

    public const string CatalogRead = "katalog.read";
    public const string CatalogManage = "katalog.manage";

    public const string PricingRead = "fiyat.read";
    public const string PricingManage = "fiyat.manage";
    public const string PricingPurchase = "fiyat.alis";
    public const string SettingsPricing = "ayarlar.fiyat";

    public const string StockRead = "stok.read";
    public const string StockManage = "stok.manage";
    public const string StockCount = "stok.sayim";
    public const string CostRead = "maliyet.read";
    public const string SettingsStock = "ayarlar.stok";

    public const string InvoiceRead = "fatura.read";
    public const string InvoiceManage = "fatura.manage";
    public const string InvoiceIssue = "fatura.issue";
    public const string InvoiceCancel = "fatura.cancel";
    public const string SettingsDocuments = "ayarlar.belgeler";
    public const string SettingsEBelge = "ayarlar.ebelge";
    public const string RateManage = "kur.manage";

    public const string FinanceRead = "finans.read";
    public const string FinanceManage = "finans.manage";
    public const string CollectionManage = "tahsilat.manage";
    public const string PaymentManage = "odeme.manage";
    public const string TransferManage = "virman.manage";
    public const string ExpenseManage = "masraf.manage";
    public const string InstrumentManage = "ceksenet.manage";
    public const string AccountingManage = "muhasebe.manage";
    public const string AccountingExport = "muhasebe.export";

    public const string PurchasingRead = "satinalma.read";
    public const string PurchasingManage = "satinalma.manage";
    public const string PurchasingReceive = "satinalma.receive";
    public const string PurchasingQuarantine = "satinalma.quarantine";
    public const string SettingsPurchasing = "ayarlar.satinalma";

    public const string SalesRead = "satis.read";
    public const string SalesManage = "satis.manage";
    public const string SalesApprove = "satis.approve";
    public const string SalesLimitOverride = "satis.limit-asimi";
    public const string SalesShip = "satis.ship";
    public const string SalesReturn = "satis.return";
    public const string SalesRecurring = "satis.recurring";
    public const string SalesTargets = "satis.targets";
    public const string SettingsSales = "ayarlar.satis";

    public const string ShippingRead = "sevkiyat.read";
    public const string ShippingPick = "sevkiyat.pick";
    public const string ShippingPack = "sevkiyat.pack";
    public const string ShippingShip = "sevkiyat.ship";
    public const string ShippingDeliver = "sevkiyat.deliver";
    public const string ShippingPlan = "sevkiyat.plan";
    public const string SettingsShipping = "ayarlar.sevkiyat";

    public const string AuditRead = "denetim.read";
}
