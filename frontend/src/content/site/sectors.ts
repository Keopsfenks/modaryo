import type { Icon } from "@tabler/icons-react";
import { IconBriefcase, IconBuildingFactory2, IconBuildingStore, IconHome2, IconShoppingBag, IconTruck } from "@tabler/icons-react";
import type { ShotKey } from "./shots";

export type StoryStep = { title: string; text: string };

export type SiteSector = {
    slug: string;
    name: string;
    summary: string;
    icon: Icon;
    /** İlgili modül slug'ları (modules.ts). */
    modules: string[];
    problem: string;
    solution: string;
    /** Sektörün bir günü: adım adım hikaye. */
    story: StoryStep[];
    shot: ShotKey;
};

export const siteSectors: SiteSector[] = [
    {
        slug: "perakende-magaza", name: "Perakende ve mağaza", summary: "Mağaza satışı, stok ve cari takibi tek panelde.", icon: IconBuildingStore,
        modules: ["perakende-pos", "stok-depo", "cari-hesaplar", "urun-katalogu"],
        problem: "Kasadaki satış ile depodaki stok ayrı kayıtlarda; gün sonunda rakamlar tutmuyor.",
        solution: "Kasa, stok ve cari aynı kayıttan beslenir; gün sonunda eldeki sayıyla kayıttaki sayı aynıdır.",
        story: [
            { title: "Kasa açılır", text: "Güne kasa açılışıyla başlarsınız; devir tutarı kayda geçer." },
            { title: "Barkod okutulur", text: "Sepet oluşur, ödeme nakit veya kartla alınır, stok anında düşer." },
            { title: "Stok uyarısı gelir", text: "Kritik seviyeye inen ürün panoda görünür." },
            { title: "Gün kapanır", text: "Kasa sayımı yapılır, farklar kayıt altına alınır." },
        ],
        shot: "pos",
    },
    {
        slug: "toptan-dagitim", name: "Toptan ve dağıtım", summary: "Bayi siparişi, sevkiyat ve vadeli satış takibi.", icon: IconTruck,
        modules: ["satis", "sevkiyat", "bayi-portali", "cari-hesaplar", "stok-depo"],
        problem: "Vadeli satışlar çoğaldıkça kimin ne kadar borcu olduğunu ve hangi sevkiyatın yolda olduğunu izlemek zorlaşıyor.",
        solution: "Sipariş, sevk, fatura ve vade takibi tek akışta; bayi bakiyesi her an bellidir.",
        story: [
            { title: "Sipariş gelir", text: "Bayi siparişi girilir, müşterinin limiti ve fiyat listesi uygulanır." },
            { title: "Sevk edilir", text: "Depo irsaliyeyi hazırlar, sevkiyat siparişe işlenir." },
            { title: "Fatura kesilir", text: "Sevk edilen kalemlerden fatura oluşur, cari bakiye güncellenir." },
            { title: "Vade izlenir", text: "Vadesi gelen tutarlar için hatırlatma alırsınız." },
        ],
        shot: "due",
    },
    {
        slug: "uretim", name: "Üretim", summary: "Hammaddeden mamule stok, reçete ve iş emri akışı.", icon: IconBuildingFactory2,
        modules: ["uretim", "stok-depo", "satin-alma", "kalite"],
        problem: "Üretime başlayınca malzemenin yetmediği anlaşılıyor; maliyet ancak ay sonunda tahmin ediliyor.",
        solution: "Reçeteden malzeme ihtiyacı çıkar, eksikler satın almaya dönüşür, maliyet iş emrinde görünür.",
        story: [
            { title: "Reçete tanımlanır", text: "Mamulün malzemeleri ve miktarları ürün ağacında durur." },
            { title: "İhtiyaç hesaplanır", text: "Eksik malzemeler için satın alma talebi açılır." },
            { title: "İş emri çalışır", text: "Malzeme stoktan düşer, mamul stoğa girer." },
            { title: "Kalite kontrol edilir", text: "Uygunsuz partiler kayda alınır ve izlenir." },
        ],
        shot: "stock",
    },
    {
        slug: "hizmet", name: "Hizmet işletmeleri", summary: "Teklif, proje ve tahsilat takibi.", icon: IconBriefcase,
        modules: ["crm", "proje", "satis", "cari-hesaplar"],
        problem: "Teklifler e-postalarda, işler başka yerde, tahsilatlar üçüncü bir listede takip ediliyor.",
        solution: "Teklif onaylanınca proje açılır, harcanan zaman ve masraf projeye yazılır, tahsilat cariden izlenir.",
        story: [
            { title: "Fırsat doğar", text: "Görüşme kaydedilir, fırsat aşamalarla ilerler." },
            { title: "Teklif verilir", text: "Fırsat tek tıkla teklife dönüşür." },
            { title: "Proje yürür", text: "Zaman ve masraf kayıtları projeye bağlanır." },
            { title: "Tahsilat alınır", text: "Aşamalı fatura kesilir, kalan tutar cari kartta görünür." },
        ],
        shot: "invoice",
    },
    {
        slug: "e-ticaret", name: "E-ticaret", summary: "Pazaryeri siparişleri, kargo ve faturalama bir arada.", icon: IconShoppingBag,
        modules: ["pazaryeri", "kargo", "e-fatura", "stok-depo"],
        problem: "Her pazaryerinin kendi paneli var; stok ve sipariş elle eşitlenince aşırı satış ve gecikme oluyor.",
        solution: "Siparişler tek listede toplanır, stok kanallarla eşitlenir, kargo ve fatura aynı ekrandan yürür.",
        story: [
            { title: "Sipariş düşer", text: "Pazaryerinden gelen sipariş ERP'de açılır." },
            { title: "Stok eşitlenir", text: "Satılabilir stok kanallara bildirilir." },
            { title: "Kargo çıkar", text: "Etiket oluşturulur, takip numarası siparişe işlenir." },
            { title: "Fatura kesilir", text: "E-fatura ya da e-arşiv aynı kayıttan gönderilir." },
        ],
        shot: "stock",
    },
    {
        slug: "hali-ev-tekstili", name: "Halı ve ev tekstili", summary: "Metrekare ölçülü satış ve taksitli tahsilat.", icon: IconHome2,
        modules: ["satis", "urun-katalogu", "cari-hesaplar", "stok-depo"],
        problem: "Halı ölçüyle satılır, bedel taksitle ödenir; bunu defterde elle hesaplamak hataya açık.",
        solution: "En ve boyu girin, metrekare fiyatı hesaplansın; bedeli taksitlere bölün, her vadeyi ayrı izleyin.",
        story: [
            { title: "Metrekareden satış", text: "m² fiyatı 2.000 ₺ olan halıdan 1,60 × 2,30 ölçüsünde satılır: 3,68 m², toplam 7.360 ₺." },
            { title: "3 taksit planı", text: "Satışta 3 taksit seçilir; ilki peşin alınır, kalan iki taksit aylık vadeyle yazılır." },
            { title: "Tedarik ve teslim", text: "Siparişe bağlı tedarikçiye sipariş geçilir, kargo takip numarası girilir, mal kabul edilip müşteriye teslim edilir." },
            { title: "Kalan borç tek ekranda", text: "Cari ve vade ekranında kalan 2 taksit tarihleriyle görünür." },
        ],
        shot: "due",
    },
];

export function getSector(slug: string): SiteSector | undefined {
    return siteSectors.find((s) => s.slug === slug);
}
