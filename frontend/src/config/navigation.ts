import {
    IconArrowsExchange,
    IconBriefcase,
    IconBuildingBank,
    IconBuildingStore,
    IconBuildingWarehouse,
    IconCalendarDue,
    IconCalendarEvent,
    IconCash,
    IconCategory,
    IconChartBar,
    IconClipboardList,
    IconFileInvoice,
    IconFileText,
    IconId,
    IconLayoutDashboard,
    IconPackage,
    IconSettings,
    IconShoppingCart,
    IconTruckDelivery,
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
            { title: "Teklifler", href: "/satis/teklifler", icon: IconFileText },
            { title: "Siparişler", href: "/satis/siparisler", icon: IconShoppingCart, badge: "12" },
            { title: "Faturalar", href: "/satis/faturalar", icon: IconFileInvoice },
        ],
    },
    {
        title: "Satın Alma",
        items: [
            { title: "Tedarikçiler", href: "/satin-alma/tedarikciler", icon: IconBuildingStore, permission: "cari.read", implemented: true },
            { title: "Satın Alma Siparişleri", href: "/satin-alma/siparisler", icon: IconTruckDelivery },
        ],
    },
    {
        title: "Stok",
        items: [
            { title: "Ürünler", href: "/stok/urunler", icon: IconPackage, permission: "katalog.read", implemented: true },
            { title: "Hizmetler", href: "/stok/hizmetler", icon: IconBriefcase, permission: "katalog.read", implemented: true },
            { title: "Kategoriler ve markalar", href: "/stok/kategoriler", icon: IconCategory, permission: "katalog.read", implemented: true },
            { title: "Depolar", href: "/stok/depolar", icon: IconBuildingWarehouse },
            { title: "Stok Hareketleri", href: "/stok/hareketler", icon: IconArrowsExchange },
        ],
    },
    {
        title: "Finans",
        items: [
            { title: "Cari Hesaplar", href: "/finans/cari-hesaplar", icon: IconWallet, permission: "cari.read", implemented: true },
            { title: "Vade Takibi", href: "/finans/vade-takibi", icon: IconCalendarDue, permission: "cari.read", implemented: true },
            { title: "Kasa & Banka", href: "/finans/kasa-banka", icon: IconBuildingBank },
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
