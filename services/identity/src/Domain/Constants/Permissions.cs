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

    public const string AuditRead = "denetim.read";
}
