import {
    IconAlertTriangle,
    IconArrowsExchange,
    IconArrowsLeftRight,
    IconBookUpload,
    IconBriefcase,
    IconBuildingBank,
    IconBuildingStore,
    IconBuildingWarehouse,
    IconCalendarDue,
    IconCalendarEvent,
    IconCash,
    IconCashBanknote,
    IconCategory,
    IconChartBar,
    IconChartLine,
    IconChecklist,
    IconClipboardCheck,
    IconClipboardList,
    IconCoins,
    IconCurrencyDollar,
    IconCurrencyEuro,
    IconFileDescription,
    IconFileInvoice,
    IconFileExport,
    IconFileText,
    IconHourglass,
    IconId,
    IconLayoutDashboard,
    IconNotes,
    IconPackage,
    IconReceipt,
    IconSettings,
    IconSpeakerphone,
    IconTag,
    IconTags,
    IconShoppingCart,
    IconTruckDelivery,
    IconUserCircle,
    IconUsers,
    IconUsersGroup,
    IconWallet,
    type Icon,
} from "@tabler/icons-react";

export type NavItem = {
    title: string;
    href: string;
    icon: Icon;
    badge?: string;
    /** Bu izni olmayan kullanıcı öğeyi menüde görmez ("*" her izni karşılar). */
    permission?: string;
    /** Ek olarak gereken ikinci izin (ör. alış faturaları: fatura.read + fiyat.alis). */
    alsoPermission?: string;
    /** Bu modül firmada kapalıysa öğe menüde görünmez. */
    module?: string;
    /** Gerçek sayfası olan öğe; "Yakında" sayfası üretilmez. */
    implemented?: boolean;
};

export type NavSection = {
    title: string;
    items: NavItem[];
};

// Sol menü. Henüz sayfası yazılmamış modüller "Yakında" sayfasına düşer
// (bkz. app/(panel)/[...slug]/page.tsx).
export const navigation: NavSection[] = [
    {
        title: "Ana Sayfa",
        items: [{ title: "Genel Bakış", href: "/panel", icon: IconLayoutDashboard }],
    },
    {
        title: "Satış",
        items: [
            { title: "Müşteriler", href: "/satis/musteriler", icon: IconUsers, permission: "cari.read", implemented: true },
            { title: "Fiyat listeleri", href: "/satis/fiyat-listeleri", icon: IconTags, permission: "fiyat.read", implemented: true },
            { title: "Müşteri grupları", href: "/satis/musteri-gruplari", icon: IconUsersGroup, permission: "fiyat.read", implemented: true },
            { title: "Kampanyalar", href: "/satis/kampanyalar", icon: IconSpeakerphone, permission: "fiyat.read", implemented: true },
            { title: "Teklifler", href: "/satis/teklifler", icon: IconFileText },
            { title: "Siparişler", href: "/satis/siparisler", icon: IconShoppingCart, badge: "12" },
            { title: "Faturalar", href: "/satis/faturalar", icon: IconFileInvoice, permission: "fatura.read", implemented: true },
        ],
    },
    {
        title: "Satın Alma",
        items: [
            { title: "Tedarikçiler", href: "/satin-alma/tedarikciler", icon: IconBuildingStore, permission: "cari.read", implemented: true },
            { title: "Alış fiyatları", href: "/satin-alma/alis-fiyatlari", icon: IconTag, permission: "fiyat.alis", implemented: true },
            { title: "Alış faturaları", href: "/satin-alma/faturalar", icon: IconFileInvoice, permission: "fatura.read", alsoPermission: "fiyat.alis", implemented: true },
            { title: "Satın Alma Siparişleri", href: "/satin-alma/siparisler", icon: IconTruckDelivery },
        ],
    },
    {
        title: "Stok",
        items: [
            { title: "Ürünler", href: "/stok/urunler", icon: IconPackage, permission: "katalog.read", implemented: true },
            { title: "Hizmetler", href: "/stok/hizmetler", icon: IconBriefcase, permission: "katalog.read", implemented: true },
            { title: "Kategoriler ve markalar", href: "/stok/kategoriler", icon: IconCategory, permission: "katalog.read", implemented: true },
            { title: "Stok durumu", href: "/stok/durum", icon: IconChecklist, permission: "stok.read", implemented: true },
            { title: "Stok Hareketleri", href: "/stok/hareketler", icon: IconArrowsExchange, permission: "stok.read", implemented: true },
            { title: "Stok fişleri", href: "/stok/fisler", icon: IconFileDescription, permission: "stok.read", implemented: true },
            { title: "Stok sayımı", href: "/stok/sayim", icon: IconClipboardCheck, permission: "stok.sayim", implemented: true },
            { title: "Depolar", href: "/stok/depolar", icon: IconBuildingWarehouse, permission: "stok.read", implemented: true },
            { title: "Minimum stok", href: "/stok/minimum", icon: IconAlertTriangle, permission: "stok.read", implemented: true },
        ],
    },
    {
        title: "Finans",
        items: [
            { title: "Cari Hesaplar", href: "/finans/cari-hesaplar", icon: IconWallet, permission: "cari.read", implemented: true },
            { title: "Vade Takibi", href: "/finans/vade-takibi", icon: IconCalendarDue, permission: "cari.read", implemented: true },
            { title: "Döviz kurları", href: "/finans/kurlar", icon: IconCurrencyDollar, permission: "fatura.read", implemented: true },
            { title: "Kasa & Banka", href: "/finans/kasa-banka", icon: IconBuildingBank, permission: "finans.read", implemented: true },
            { title: "Tahsilat", href: "/finans/tahsilat/yeni", icon: IconCoins, permission: "tahsilat.manage", implemented: true },
            { title: "Ödeme", href: "/finans/odeme/yeni", icon: IconCashBanknote, permission: "odeme.manage", implemented: true },
            { title: "Virman", href: "/finans/virman/yeni", icon: IconArrowsLeftRight, permission: "virman.manage", implemented: true },
            { title: "Masraflar", href: "/finans/masraf", icon: IconReceipt, permission: "masraf.manage", implemented: true },
            { title: "Personel carileri", href: "/finans/personel-carileri", icon: IconUserCircle, permission: "cari.read", implemented: true },
            { title: "Çek / Senet", href: "/finans/cek-senet", icon: IconNotes, permission: "finans.read", module: "cek-senet", implemented: true },
            { title: "Kur farkı", href: "/finans/kur-farki", icon: IconCurrencyEuro, permission: "finans.read", implemented: true },
            { title: "Mahsup fişleri", href: "/finans/mahsup", icon: IconBookUpload, permission: "finans.read", implemented: true },
            { title: "Yaşlandırma", href: "/finans/yaslandirma", icon: IconHourglass, permission: "finans.read", implemented: true },
            { title: "Nakit akışı", href: "/finans/nakit-akisi", icon: IconChartLine, permission: "finans.read", implemented: true },
            { title: "Muhasebe aktarımı", href: "/finans/muhasebe-aktarimi", icon: IconFileExport, permission: "muhasebe.export", implemented: true },
            { title: "Raporlar", href: "/finans/raporlar", icon: IconChartBar },
        ],
    },
    {
        title: "İnsan Kaynakları",
        items: [
            { title: "Personel", href: "/ik/personel", icon: IconId },
            { title: "İzinler", href: "/ik/izinler", icon: IconCalendarEvent },
            { title: "Bordro", href: "/ik/bordro", icon: IconCash },
        ],
    },
    {
        title: "Sistem",
        items: [
            { title: "Kullanıcılar", href: "/ayarlar/kullanicilar", icon: IconUsersGroup, permission: "members.read", implemented: true },
            { title: "Görevler", href: "/sistem/gorevler", icon: IconClipboardList },
            { title: "Ayarlar", href: "/ayarlar", icon: IconSettings, implemented: true },
        ],
    },
];

export const allNavItems: NavItem[] = navigation.flatMap((section) => section.items);

export function findNavItem(href: string): NavItem | undefined {
    return allNavItems.find((item) => item.href === href);
}
