using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("IdentityService.Domain.UnitTests")]
[assembly: InternalsVisibleTo("IdentityService.Web.FunctionalTests")]

namespace IdentityService.Domain.Constants;

/// <summary>Katalogdaki tek bir izin. ModuleKey null ise çekirdek izindir; doluysa yalnızca o modül açık firmalara görünür.</summary>
public sealed record PermissionInfo(string Code, string? ModuleKey, string Group, string Name, string Description);

/// <summary>
/// Kod tanımlı izin kataloğu (tek kaynak). Rol düzenleyici bu listeyi gruplu gösterir.
/// Yeni modüller girişlerini aşağıdaki statik kurucuda Register ile ekler. "*" katalog girdisi değildir.
/// </summary>
public static class PermissionCatalog
{
    private static readonly List<PermissionInfo> Entries = [];
    private static readonly HashSet<string> Codes = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static readonly int CoreCount;

    static PermissionCatalog()
    {
        Register(
            new(Permissions.CompanyUpdate, null, "Firma", "Firma bilgilerini düzenle", "Firma adı ve genel bilgilerini güncelleyebilir."),

            new(Permissions.MembersRead, null, "Üyeler", "Üyeleri görüntüle", "Firmanın üyelerini ve rollerini listeleyebilir."),
            new(Permissions.MembersInvite, null, "Üyeler", "Üye davet et", "Yeni üye daveti gönderebilir, yeniden gönderebilir veya iptal edebilir."),
            new(Permissions.MembersManage, null, "Üyeler", "Üyeleri yönet", "Üyelerin rolünü değiştirebilir ve üyeleri firmadan çıkarabilir."),

            new(Permissions.RolesRead, null, "Roller", "Rolleri görüntüle", "Rolleri ve izin listesini görüntüleyebilir."),
            new(Permissions.RolesManage, null, "Roller", "Rolleri yönet", "Rol oluşturabilir, düzenleyebilir ve silebilir."),

            new(Permissions.SettingsModules, null, "Ayarlar", "Modülleri yönet", "Firma için modülleri açıp kapatabilir."),
            new(Permissions.SettingsBranches, null, "Ayarlar", "Şubeleri yönet", "Şube tanımlayabilir ve düzenleyebilir."),
            new(Permissions.SettingsCompany, null, "Ayarlar", "Firma bilgilerini yönet", "Unvan, vergi bilgileri, adres ve banka hesaplarını görüntüleyip düzenleyebilir."),
            new(Permissions.SettingsGeneral, null, "Ayarlar", "Genel ayarları yönet", "Para birimi, ondalık basamak, çalışma günleri ve tatil günlerini düzenleyebilir."),
            new(Permissions.SettingsFinance, null, "Ayarlar", "Mali ayarları yönet", "Dönem kilidi gibi mali ayarları düzenleyebilir."),

            new(Permissions.CariRead, null, "Cari hesaplar", "Cari hesapları görüntüle", "Cari hesapları, ekstreleri ve vade bilgilerini görüntüleyebilir."),
            new(Permissions.CariManage, null, "Cari hesaplar", "Cari hesapları yönet", "Cari hesap oluşturabilir, düzenleyebilir ve pasife alabilir."),
            new(Permissions.CariMovements, null, "Cari hesaplar", "Cari hareketleri gir", "Cari hesaplara borç, alacak ve tahsilat hareketi girebilir."),

            new(Permissions.CatalogRead, null, "Katalog", "Ürün ve hizmetleri görüntüle", "Ürün, hizmet, kategori ve markaları görüntüleyebilir."),
            new(Permissions.CatalogManage, null, "Katalog", "Ürün ve hizmetleri yönet", "Ürün, hizmet, kategori ve marka oluşturabilir, düzenleyebilir, içe aktarabilir."),
            new(Permissions.SettingsCatalog, null, "Katalog", "Katalog ayarlarını yönet", "Ürün kodu şablonu, birimler, KDV oranı, barkod öneki ve özel alanları düzenleyebilir."),

            new(Permissions.PricingRead, null, "Fiyatlandırma", "Satış fiyatlarını görüntüle", "Satış fiyat listelerini ve fiyatları görüntüleyebilir."),
            new(Permissions.PricingManage, null, "Fiyatlandırma", "Fiyatları yönet", "Fiyat listelerini, müşteriye özel fiyatları ve kampanyaları düzenleyebilir."),
            new(Permissions.PricingPurchase, null, "Fiyatlandırma", "Alış fiyatlarını gör", "Alış fiyatlarını ve alış fiyat listelerini her yerde görüntüleyebilir."),
            new(Permissions.SettingsPricing, null, "Fiyatlandırma", "Fiyatlandırma ayarlarını yönet", "Varsayılan fiyat listeleri, KDV gösterimi ve yuvarlama kuralını düzenleyebilir."),

            new(Permissions.StockRead, null, "Stok", "Stoğu görüntüle", "Stok durumunu, hareketleri ve depoları görüntüleyebilir."),
            new(Permissions.StockManage, null, "Stok", "Stoğu yönet", "Stok fişleri, transferler, depolar ve minimum stok seviyelerini düzenleyebilir."),
            new(Permissions.StockCount, null, "Stok", "Stok sayımı yap", "Stok sayımı başlatabilir, girebilir ve onaylayabilir."),
            new(Permissions.CostRead, null, "Stok", "Maliyetleri gör", "Birim maliyetleri ve stok değerlemesini her yerde görüntüleyebilir."),
            new(Permissions.SettingsStock, null, "Stok", "Stok ayarlarını yönet", "Maliyet yöntemini ve eksi stok ayarını düzenleyebilir."),

            new(Permissions.InvoiceRead, null, "Fatura", "Faturaları görüntüle", "Satış ve alış faturalarını görüntüleyebilir."),
            new(Permissions.InvoiceManage, null, "Fatura", "Fatura taslaklarını yönet", "Fatura taslağı oluşturabilir, düzenleyebilir ve silebilir."),
            new(Permissions.InvoiceIssue, null, "Fatura", "Fatura kes", "Taslak faturayı kesebilir; numara atanır, cari ve stok işlenir."),
            new(Permissions.InvoiceCancel, null, "Fatura", "Fatura iptal et", "Kesilmiş faturayı gerekçesiyle iptal edebilir."),
            new(Permissions.SettingsDocuments, null, "Fatura", "Belge ayarlarını yönet", "Belge serilerini, PDF görünümünü ve e-posta şablonlarını düzenleyebilir."),
            new(Permissions.SettingsEBelge, null, "Fatura", "e-Belge ayarlarını yönet", "e-Fatura / e-Arşiv kullanımını ve varsayılan senaryoyu düzenleyebilir."),
            new(Permissions.RateManage, null, "Fatura", "Kurları yönet", "Döviz kurlarını elle girebilir ve düzeltebilir."),

            new(Permissions.FinanceRead, null, "Finans", "Kasa, banka ve finans belgelerini görüntüle", "Kasa/banka hesaplarını, hareketleri, tahsilat, ödeme ve mahsup fişlerini görüntüleyebilir."),
            new(Permissions.FinanceManage, null, "Finans", "Kasa ve banka hesaplarını yönet", "Kasa, banka ve kart POS hesapları oluşturabilir, düzenleyebilir ve pasifleştirebilir."),
            new(Permissions.CollectionManage, null, "Finans", "Tahsilat yap", "Tahsilat belgesi oluşturabilir, iptal edebilir ve elle kapatma yapabilir."),
            new(Permissions.PaymentManage, null, "Finans", "Ödeme yap", "Ödeme belgesi oluşturabilir, iptal edebilir ve elle kapatma yapabilir."),
            new(Permissions.TransferManage, null, "Finans", "Virman yap", "Hesaplar arası virman belgesi oluşturabilir ve iptal edebilir."),
            new(Permissions.ExpenseManage, null, "Finans", "Masraf ve gelir fişi gir", "Masraf ve gelir fişi oluşturabilir ve iptal edebilir."),
            new(Permissions.InstrumentManage, "cek-senet", "Finans", "Çek ve senetleri yönet", "Çek/senet kaydedebilir ve durumunu (ciro, tahsil, iade, ödeme) değiştirebilir."),
            new(Permissions.AccountingManage, null, "Finans", "Mahsup fişi ve hesap eşlemesi", "Mahsup fişi girebilir ve muhasebe hesap eşlemesini düzenleyebilir."),
            new(Permissions.AccountingExport, null, "Finans", "Muhasebe aktarımı", "Muhasebe aktarım dosyasını indirebilir."),

            new(Permissions.PurchasingRead, null, "Satın alma", "Satın alma belgelerini görüntüle", "Satın alma siparişlerini, mal kabulleri, masraf dağıtımlarını ve ithalat dosyalarını görüntüleyebilir."),
            new(Permissions.PurchasingManage, null, "Satın alma", "Satın alma siparişi ve masraf dağıtımı yönet", "Sipariş oluşturabilir, onaylayabilir, kapatabilir; masraf dağıtımı ve ithalat dosyası yönetebilir."),
            new(Permissions.PurchasingReceive, null, "Satın alma", "Mal kabul yap", "Mal kabul belgesi oluşturabilir ve iptal edebilir; fiyat göremeden yalnızca miktar girebilir."),
            new(Permissions.PurchasingQuarantine, null, "Satın alma", "Karantinadaki malı yönet", "Karantinadaki malı depoya alabilir ya da reddedebilir."),
            new(Permissions.SettingsPurchasing, null, "Satın alma", "Satın alma ayarlarını yönet", "Fazla teslimat toleransını, fiyat farkı eşiğini ve karantina ayarlarını düzenleyebilir."),

            new(Permissions.SalesRead, null, "Satış", "Satış belgelerini görüntüle", "Teklif, sipariş, sevk, iade ve tekrarlayan fatura şablonlarını görüntüleyebilir."),
            new(Permissions.SalesManage, null, "Satış", "Teklif ve sipariş yönet", "Teklif ve sipariş oluşturabilir, düzenleyebilir, gönderebilir ve siparişe dönüştürebilir."),
            new(Permissions.SalesApprove, null, "Satış", "Siparişi onayla", "Satış siparişini onaylayabilir ve iptal edebilir."),
            new(Permissions.SalesLimitOverride, null, "Satış", "Risk limiti aşımını onayla", "Cari risk limitini aşan siparişin onayını verebilir."),
            new(Permissions.SalesShip, null, "Satış", "Sevkiyat yap", "Sevk belgesi oluşturabilir, sevk edebilir ve iptal edebilir."),
            new(Permissions.SalesReturn, null, "Satış", "Satış iadesi al", "Kesilmiş faturadan satış iadesi oluşturabilir."),
            new(Permissions.SalesRecurring, null, "Satış", "Tekrarlayan faturaları yönet", "Abonelik (tekrarlayan fatura) şablonlarını yönetebilir."),
            new(Permissions.SalesTargets, null, "Satış", "Satış hedeflerini yönet", "Satış temsilcisi hedeflerini girebilir ve gerçekleşmeyi görebilir."),
            new(Permissions.SettingsSales, null, "Satış", "Satış ayarlarını yönet", "Teklif geçerlilik süresi, satış temsilcisi zorunluluğu ve taksit varsayılanlarını düzenleyebilir."),

            new(Permissions.AuditRead, null, "Denetim", "Denetim kaydını görüntüle", "Firmadaki işlem geçmişini ve denetim kayıtlarını görüntüleyebilir."));
        CoreCount = Entries.Count;
    }

    public static IReadOnlyList<PermissionInfo> All
    {
        get
        {
            lock (Gate)
            {
                return Entries.ToArray();
            }
        }
    }

    public static bool IsKnown(string code)
    {
        lock (Gate)
        {
            return Codes.Contains(code);
        }
    }

    /// <summary>Yalnızca statik kurucudan çağrılır; mükerrer kod programlama hatasıdır.</summary>
    public static void Register(params PermissionInfo[] entries)
    {
        lock (Gate)
        {
            foreach (var entry in entries)
            {
                if (!Codes.Add(entry.Code))
                {
                    throw new InvalidOperationException($"İzin kodu zaten kayıtlı: {entry.Code}");
                }

                Entries.Add(entry);
            }
        }
    }

    internal static void RegisterForTests(PermissionInfo entry) => Register(entry);

    /// <summary>Testlerde eklenen girdileri kaldırır; kurucudaki girdiler kalır.</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            for (var i = Entries.Count - 1; i >= CoreCount; i--)
            {
                Codes.Remove(Entries[i].Code);
                Entries.RemoveAt(i);
            }
        }
    }
}
