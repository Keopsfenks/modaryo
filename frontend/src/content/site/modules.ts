import type { Icon } from "@tabler/icons-react";
import {
    IconBell,
    IconBox,
    IconBuildingCommunity,
    IconBuildingFactory2,
    IconBuildingStore,
    IconBuildingWarehouse,
    IconCalculator,
    IconCashRegister,
    IconCertificate,
    IconChartBar,
    IconChecklist,
    IconDeviceDesktop,
    IconFileInvoice,
    IconHeartHandshake,
    IconId,
    IconReceipt2,
    IconShoppingBag,
    IconShoppingCart,
    IconTags,
    IconTool,
    IconTruck,
    IconTruckDelivery,
    IconUsers,
} from "@tabler/icons-react";

export type SiteModule = {
    slug: string;
    name: string;
    summary: string;
    icon: Icon;
    status: "available" | "coming";
    group: "Satış" | "Stok ve Satın Alma" | "Finans" | "Operasyon" | "Sektör";
    specRef: string;
};

export const moduleGroups: SiteModule["group"][] = ["Satış", "Stok ve Satın Alma", "Finans", "Operasyon"];

/** Yalnızca gerçekten geliştirilmiş modüller "available" olur; diğerleri "Yakında" olarak gösterilir. */
export const siteModules: SiteModule[] = [
    { slug: "cari-hesaplar", name: "Cari hesaplar", summary: "Müşteri ve tedarikçi bakiyeleri, hareketler ve vade takibi tek yerde.", icon: IconUsers, status: "available", group: "Finans", specRef: "—" },
    { slug: "urun-katalogu", name: "Ürün kataloğu", summary: "Ürünler, varyantlar, birimler ve kategoriler düzenli bir katalogda.", icon: IconBox, status: "coming", group: "Satış", specRef: "§1" },
    { slug: "fiyatlandirma", name: "Fiyatlandırma", summary: "Fiyat listeleri, iskontolar ve müşteriye özel fiyat kuralları.", icon: IconTags, status: "coming", group: "Satış", specRef: "§2" },
    { slug: "stok-depo", name: "Stok ve depo", summary: "Depo bazlı stok, hareketler ve minimum stok uyarıları.", icon: IconBuildingWarehouse, status: "coming", group: "Stok ve Satın Alma", specRef: "§3" },
    { slug: "satin-alma", name: "Satın alma", summary: "Talepten teklife, siparişten mal kabule satın alma süreci.", icon: IconShoppingCart, status: "coming", group: "Stok ve Satın Alma", specRef: "§4" },
    { slug: "satis", name: "Satış", summary: "Teklif, sipariş ve faturayı tek akışta yönetin.", icon: IconReceipt2, status: "coming", group: "Satış", specRef: "§5" },
    { slug: "perakende-pos", name: "Perakende ve POS", summary: "Mağaza satışı, kasa ve günlük kapanış.", icon: IconCashRegister, status: "coming", group: "Satış", specRef: "§5b" },
    { slug: "sevkiyat", name: "Sevkiyat", summary: "İrsaliye, sevk planı ve teslimat takibi.", icon: IconTruckDelivery, status: "coming", group: "Stok ve Satın Alma", specRef: "§6" },
    { slug: "e-fatura", name: "E-fatura", summary: "E-fatura ve e-arşiv belgelerini ERP içinden düzenleyin.", icon: IconFileInvoice, status: "coming", group: "Finans", specRef: "§7" },
    { slug: "on-muhasebe", name: "Ön muhasebe", summary: "Kasa, banka ve çek-senet hareketleri; mali müşavirinize hazır kayıtlar.", icon: IconCalculator, status: "coming", group: "Finans", specRef: "§8" },
    { slug: "raporlama", name: "Raporlama", summary: "Satış, stok ve cari raporları; panoda anlık özet.", icon: IconChartBar, status: "coming", group: "Finans", specRef: "§9" },
    { slug: "bildirimler", name: "Bildirimler", summary: "Vade, stok ve onay hatırlatmaları e-posta ve panoda.", icon: IconBell, status: "coming", group: "Operasyon", specRef: "§10" },
    { slug: "pazaryeri", name: "Pazaryeri entegrasyonu", summary: "Pazaryeri siparişleri ve stokları ERP ile senkron.", icon: IconShoppingBag, status: "coming", group: "Satış", specRef: "§12a" },
    { slug: "kargo", name: "Kargo entegrasyonu", summary: "Kargo etiketi oluşturma ve gönderi takibi.", icon: IconTruck, status: "coming", group: "Stok ve Satın Alma", specRef: "§12b" },
    { slug: "crm", name: "CRM", summary: "Müşteri görüşmeleri, fırsatlar ve takip görevleri.", icon: IconHeartHandshake, status: "coming", group: "Satış", specRef: "§12c" },
    { slug: "bayi-portali", name: "Bayi portalı", summary: "Bayileriniz sipariş verir, bakiyesini ve belgelerini görür.", icon: IconBuildingCommunity, status: "coming", group: "Satış", specRef: "§12d" },
    { slug: "insan-kaynaklari", name: "İnsan kaynakları", summary: "Personel kartları, izinler ve özlük belgeleri.", icon: IconId, status: "coming", group: "Operasyon", specRef: "§12e" },
    { slug: "sabit-kiymet", name: "Sabit kıymet", summary: "Demirbaş kayıtları ve amortisman takibi.", icon: IconDeviceDesktop, status: "coming", group: "Finans", specRef: "§12f" },
    { slug: "teknik-servis", name: "Teknik servis", summary: "Servis kayıtları, parça kullanımı ve müşteri bilgilendirme.", icon: IconTool, status: "coming", group: "Operasyon", specRef: "§12g" },
    { slug: "uretim", name: "Üretim", summary: "Reçeteler, iş emirleri ve üretim maliyeti.", icon: IconBuildingFactory2, status: "coming", group: "Operasyon", specRef: "§12h" },
    { slug: "depo-yonetimi", name: "Depo yönetimi", summary: "Raf ve lokasyon, sayım ve transfer işlemleri.", icon: IconBuildingStore, status: "coming", group: "Stok ve Satın Alma", specRef: "§12i" },
    { slug: "kalite", name: "Kalite", summary: "Kalite kontrol kayıtları ve uygunsuzluk takibi.", icon: IconCertificate, status: "coming", group: "Operasyon", specRef: "§12j" },
    { slug: "proje", name: "Proje", summary: "Proje bazlı görev, maliyet ve zaman takibi.", icon: IconChecklist, status: "coming", group: "Operasyon", specRef: "§12k" },
];

export function getModule(slug: string): SiteModule | undefined {
    return siteModules.find((m) => m.slug === slug);
}
