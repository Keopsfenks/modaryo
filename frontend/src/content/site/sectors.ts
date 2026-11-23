import type { Icon } from "@tabler/icons-react";
import { IconBriefcase, IconBuildingFactory2, IconBuildingStore, IconHome2, IconShoppingBag, IconTruck } from "@tabler/icons-react";

export type SiteSector = {
    slug: string;
    name: string;
    summary: string;
    icon: Icon;
    /** İlgili modül slug'ları (modules.ts). */
    modules: string[];
};

export const siteSectors: SiteSector[] = [
    { slug: "perakende-magaza", name: "Perakende ve mağaza", summary: "Mağaza satışı, stok ve cari takibi tek panelde.", icon: IconBuildingStore, modules: ["perakende-pos", "stok-depo", "cari-hesaplar", "urun-katalogu"] },
    { slug: "toptan-dagitim", name: "Toptan ve dağıtım", summary: "Bayi siparişi, sevkiyat ve vadeli satış takibi.", icon: IconTruck, modules: ["satis", "sevkiyat", "bayi-portali", "cari-hesaplar", "stok-depo"] },
    { slug: "uretim", name: "Üretim", summary: "Hammaddeden mamule stok, reçete ve iş emri akışı.", icon: IconBuildingFactory2, modules: ["uretim", "stok-depo", "satin-alma", "kalite"] },
    { slug: "hizmet", name: "Hizmet işletmeleri", summary: "Teklif, proje ve tahsilat takibi.", icon: IconBriefcase, modules: ["crm", "proje", "satis", "cari-hesaplar"] },
    { slug: "e-ticaret", name: "E-ticaret", summary: "Pazaryeri siparişleri, kargo ve faturalama bir arada.", icon: IconShoppingBag, modules: ["pazaryeri", "kargo", "e-fatura", "stok-depo"] },
    { slug: "hali-ev-tekstili", name: "Halı ve ev tekstili", summary: "Metrekare ölçülü satış ve taksitli tahsilat.", icon: IconHome2, modules: ["satis", "urun-katalogu", "cari-hesaplar", "stok-depo"] },
];

export function getSector(slug: string): SiteSector | undefined {
    return siteSectors.find((s) => s.slug === slug);
}
