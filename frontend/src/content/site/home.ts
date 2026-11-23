import type { ShotKey } from "./shots";

export const hero = {
    title: "Halıyı metrekareden sat, 3 taksite böl, kalan borcu tek ekranda gör.",
    subline: "Modaryo; cari hesapları, stoğu, satışı ve ön muhasebeyi tek panelde toplar. Kurulum yok, şu an tüm modüller ücretsiz.",
};

/** Ana sayfadaki altı özet kart (modül slug'ları). */
export const highlightSlugs = ["cari-hesaplar", "satis", "stok-depo", "e-fatura", "perakende-pos", "raporlama"];

export type SectorStory = { slug: string; headline: string; text: string; shot: ShotKey };

export const sectorStories: SectorStory[] = [
    { slug: "perakende-magaza", headline: "Kasadan depoya aynı rakam", text: "Mağazada satılan ürün anında stoktan düşer, gün sonunda kasa ve cari bakiyeler kendiliğinden tutar.", shot: "pos" },
    { slug: "toptan-dagitim", headline: "Vadeli satışı ve bayi bakiyesini kaçırmayın", text: "Siparişi sevk edin, faturayı kesin, hangi bayinin ne kadar borcu olduğunu ve vadesinin ne zaman geldiğini görün.", shot: "due" },
    { slug: "uretim", headline: "Hammadde bitmeden haberiniz olsun", text: "Reçetedeki malzemelerin stoğunu izleyin, kritik seviyeye inen ürünler için satın alma talebi açın.", shot: "stock" },
    { slug: "hizmet", headline: "Teklif verin, işi takip edin, tahsil edin", text: "Müşteriye gönderdiğiniz teklif onaylanınca faturaya döner; kimin ne kadar borcu kaldığı cari ekranda durur.", shot: "invoice" },
    { slug: "e-ticaret", headline: "Sipariş, stok ve fatura aynı yerde", text: "Pazaryerinden gelen siparişle stok düşsün, fatura hazırlansın, kargo bilgisi siparişin üzerinde dursun.", shot: "stock" },
    { slug: "hali-ev-tekstili", headline: "Metrekareden sat, taksitle tahsil et", text: "Halıyı ölçüsüne göre fiyatlayın, bedeli 3 taksite bölün ve kalan borcu müşterinin kartında takip edin.", shot: "due" },
];

export const flowSteps = [
    { title: "Teklif", text: "Müşteriye fiyatı ve vadeyi içeren teklifi birkaç satırda hazırlayın." },
    { title: "Sipariş", text: "Onaylanan teklif tek tıkla siparişe döner, stok ayrılır." },
    { title: "Sevk", text: "İrsaliyeyi oluşturun; depo çıkışı ve teslimat durumu siparişe işlenir." },
    { title: "Fatura", text: "Sevk edilen kalemlerden fatura kesin, cari bakiye kendiliğinden güncellensin." },
    { title: "Tahsilat", text: "Gelen ödemeyi faturayla eşleştirin; kalan borç ve vade bir bakışta görünsün." },
];

export const integrationGroups = [
    { title: "e-Belge", items: ["e-Fatura", "e-Arşiv", "e-İrsaliye", "GİB"] },
    { title: "Pazaryeri", items: ["Trendyol", "Hepsiburada", "N11", "Amazon Türkiye"] },
    { title: "Kargo", items: ["Yurtiçi Kargo", "Aras Kargo", "MNG Kargo", "Sürat Kargo"] },
    { title: "Banka ve ödeme", items: ["Banka hesap hareketleri", "iyzico", "PayTR"] },
];

export const securityItems = [
    { title: "KVKK", text: "Kişisel veriler KVKK çerçevesinde işlenir. Aydınlatma ve gizlilik metinleri sitede açıkça yayınlanır." },
    { title: "Firmaya özel veri", text: "Her firmanın kayıtları diğerlerinden ayrı tutulur; bir firmanın verisi başkasına görünmez." },
    { title: "Rol ve yetki", text: "Kullanıcılara yalnızca işleri için gereken ekranları ve şubeleri açarsınız." },
    { title: "Denetim kaydı", text: "Kim neyi ne zaman değiştirdi, kayıtlardan geriye dönük görebilirsiniz." },
];
