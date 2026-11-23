import type { FaqItem } from "./faq";
import type { ShotKey } from "./shots";

export type Feature = { title: string; text: string };

export type ModuleBody = {
    problem: string;
    solution: string;
    features: Feature[];
    shot: ShotKey;
    faq?: FaqItem[];
};

/** Modül sayfası gövdeleri; içerik ERP tasarım belgesindeki ilgili bölümden derlenmiştir. */
export const moduleBodies: Record<string, ModuleBody> = {
    "cari-hesaplar": {
        problem: "Kim ne kadar borçlu, hangi tedarikçiye ne kadar ödeme kaldı? Cevaplar defterde, Excel'de ve kafada dağınık duruyor.",
        solution: "Her müşteri ve tedarikçi için tek bir cari kart: bakiye, hareketler ve vadesi geçen tutarlar tek ekranda.",
        features: [
            { title: "Borç ve alacak bakiyesi", text: "Bakiye (B) ve (A) olarak net görünür; müşteri ile tedarikçi aynı kartta birleşebilir." },
            { title: "Hareket dökümü", text: "Fatura, tahsilat ve ödemeler tarih sırasıyla cari ekstrede listelenir." },
            { title: "Vade takibi", text: "Vadesi geçmiş tutar ayrıca gösterilir; geciken carileri hemen bulursunuz." },
            { title: "Şubeye ve role göre erişim", text: "Her kullanıcı yalnızca yetkili olduğu carileri ve şubeleri görür." },
        ],
        shot: "cari",
        faq: [{ question: "Aynı kişi hem müşteri hem tedarikçi olabilir mi?", answer: "Evet. Bir cari kartta iki rolü birlikte tanımlayabilirsiniz; bakiye tek yerde görünür." }],
    },
    "urun-katalogu": {
        problem: "Aynı ürünün renk ve ebat seçenekleri ayrı satırlarda çoğalıyor, kodlar ve birimler karışıyor.",
        solution: "Ürünleri bir kez tanımlayın; varyantları, birimleri ve barkodları kartın içinde tutun.",
        features: [
            { title: "Varyantlar", text: "Renk, ebat gibi seçenekler için her varyantın kendi kodu, barkodu ve stoğu olur." },
            { title: "Birim dönüşümü", text: "Adet, koli ve metrekare gibi birimler arasında dönüşüm tanımlanır." },
            { title: "Tedarikçi kodları", text: "Her tedarikçinin kendi ürün kodunu karta ekleyin; sipariş verirken doğru kodu kullanın." },
            { title: "Excel ile aktarım", text: "Mevcut ürün listenizi Excel'den içeri alın, katalogu dışarı aktarın." },
        ],
        shot: "stock",
    },
    "fiyatlandirma": {
        problem: "Müşteriye göre değişen fiyatlar ve iskontolar ezberde kalıyor, yanlış fiyat kesilen faturalara yol açıyor.",
        solution: "Fiyat listeleri ve kurallar satır fiyatını sizin yerinize belirler.",
        features: [
            { title: "Fiyat listeleri", text: "Perakende, toptan ve bayi gibi birden çok liste tanımlayın." },
            { title: "Müşteriye özel fiyat", text: "Belirli bir müşteri veya ürün için listeyi geçersiz kılan özel fiyat verin." },
            { title: "Öncelik sırası", text: "Hangi kuralın önce uygulanacağı belirlidir; satırdaki fiyat nereden geldiği bellidir." },
            { title: "İskonto", text: "Satır ve belge iskontoları fiyat kurallarıyla birlikte hesaplanır." },
        ],
        shot: "invoice",
    },
    "stok-depo": {
        problem: "Raftaki ile kayıttaki stok tutmuyor; kritik ürün bittiğinde geç öğreniyorsunuz.",
        solution: "Depo bazlı stok, her hareketin izini ve minimum stok uyarısını tek yerde tutar.",
        features: [
            { title: "Çoklu depo", text: "Depolar arasında transfer yapın, her depodaki miktarı ayrı görün." },
            { title: "Stok hareketleri", text: "Giriş, çıkış, sayım ve transferler kayıt altında; her miktarın nereden geldiği görünür." },
            { title: "Minimum stok uyarısı", text: "Kritik seviyeye inen ürünler panoda ve bildirimde listelenir." },
            { title: "Seri ve lot takibi", text: "Gerektiğinde ürünleri seri numarası veya lot ile izleyin." },
        ],
        shot: "stock",
    },
    "satin-alma": {
        problem: "Tedarikçiye verilen sipariş, gelen mal ve gelen fatura birbirinden kopuk takip ediliyor.",
        solution: "Talepten teklife, siparişten mal kabule kadar her adım bir öncekine bağlı ilerler.",
        features: [
            { title: "Talep ve teklif", text: "Birden çok tedarikçiden teklif alın, fiyatları yan yana karşılaştırın." },
            { title: "Satın alma siparişi", text: "Onaylanan teklif tek tıkla siparişe döner." },
            { title: "Mal kabul", text: "Gelen malı sipariş satırlarıyla karşılaştırarak kabul edin; stok kendiliğinden artar." },
            { title: "Masraf dağıtımı", text: "Nakliye ve gümrük gibi masrafları kalemlere dağıtarak gerçek maliyeti görün." },
        ],
        shot: "invoice",
    },
    "satis": {
        problem: "Teklif bir dosyada, sipariş başka bir yerde, fatura üçüncü bir programda; her adımda aynı bilgi yeniden yazılıyor.",
        solution: "Teklif, sipariş ve fatura tek akışta; her belge bir öncekinden devam eder.",
        features: [
            { title: "Teklif ve sipariş", text: "Teklifi onaylanınca siparişe çevirin, stok ayrılsın." },
            { title: "Ölçülü satış", text: "En ve boy girin, miktar metrekare olarak hesaplansın: 1,60 × 2,30 = 3,68 m²." },
            { title: "Taksitli ödeme planı", text: "Peşinat ve taksitleri belirleyin; her taksit kendi vadesiyle ayrı bir kalem olur." },
            { title: "Kredi limiti", text: "Müşterinin limiti aşıldığında satış sırasında uyarı alırsınız." },
        ],
        shot: "invoice",
    },
    "perakende-pos": {
        problem: "Mağaza kasası ile depo kaydı ayrı çalışınca gün sonunda rakamlar tutmuyor.",
        solution: "Hızlı satış ekranı kasayı, stoğu ve cariyi aynı kayıttan besler.",
        features: [
            { title: "Hızlı satış ekranı", text: "Barkod okutun, sepeti oluşturun, ödeme türünü seçip bitirin." },
            { title: "Kasa yönetimi", text: "Kasa açılışı, gün içi hareketler ve kapanış sayımı kayıt altındadır." },
            { title: "Karışık ödeme", text: "Bir satışı nakit ve kartla bölerek tahsil edin." },
            { title: "Anlık stok düşümü", text: "Satılan ürün satış anında stoktan düşer." },
        ],
        shot: "pos",
    },
    "sevkiyat": {
        problem: "Hangi sipariş yola çıktı, hangisi teslim edildi? Telefonla sormadan öğrenemiyorsunuz.",
        solution: "Siparişten irsaliye oluşturun, sevkiyatı ve teslimatı siparişin üzerinden izleyin.",
        features: [
            { title: "İrsaliye", text: "Sipariş kalemlerinden kısmi veya tam irsaliye oluşturun." },
            { title: "Sevk planı", text: "Hangi gün hangi siparişin çıkacağını planlayın." },
            { title: "Teslimat durumu", text: "Hazırlandı, yolda ve teslim edildi adımları siparişte görünür." },
            { title: "Kargo takip numarası", text: "Takip numarasını sevke işleyin, müşteriyle paylaşın." },
        ],
        shot: "stock",
    },
    "e-fatura": {
        problem: "Faturayı bir yerde hazırlayıp başka bir portalda yeniden kesmek hem zaman alıyor hem hata getiriyor.",
        solution: "Faturayı ERP içinden düzenleyin; e-fatura ve e-arşiv belgesi aynı kayıttan çıksın.",
        features: [
            { title: "Faturadan e-belgeye", text: "Satış kaydı tamamlanınca e-fatura veya e-arşiv belgesi ERP'den gönderilir." },
            { title: "Mükellef sorgusu", text: "Müşterinin e-fatura mükellefi olup olmadığı belge türünü belirler." },
            { title: "Durum takibi", text: "Gönderilen belgenin durumu fatura üzerinde görünür." },
            { title: "Entegratör bağlantısı", text: "Belge gönderimi anlaşmalı özel entegratör üzerinden yapılır." },
        ],
        shot: "invoice",
    },
    "on-muhasebe": {
        problem: "Kasa, banka ve çek-senet hareketleri mali müşavire gitmeden önce toparlanmak zorunda kalıyor.",
        solution: "Nakit hareketlerini düzenli tutun, mali müşavirinize hazır kayıt teslim edin.",
        features: [
            { title: "Kasa ve banka", text: "Tahsilat ve ödemeler hesaplara işlenir, bakiyeler her an görünür." },
            { title: "Çek ve senet", text: "Portföydeki çek ve senetleri vadesine göre izleyin." },
            { title: "Nakit akışı", text: "Yaklaşan tahsilat ve ödemelerden basit bir projeksiyon görün." },
            { title: "Muhasebe aktarımı", text: "Kayıtları mali müşavirinizin programına aktarın." },
        ],
        shot: "due",
    },
    "raporlama": {
        problem: "Satış, stok ve cari rakamları için ayrı ayrı tablolar hazırlamak her ay saatler alıyor.",
        solution: "Hazır raporlar ve anlık panoyla rakamlar tek bakışta karşınızda.",
        features: [
            { title: "Yönetim paneli", text: "Aylık satış, tahsilat, vadesi geçen tutar ve kritik stok ana sayfada durur." },
            { title: "Hazır rapor şablonları", text: "Sık kullanılan raporlar hazır gelir; kendi ihtiyacınıza göre düzenlersiniz." },
            { title: "Rapor tasarlayıcı", text: "Sütun ve süzgeçleri seçerek kendi raporunuzu oluşturun." },
            { title: "Dışa aktarım", text: "Raporları Excel olarak alıp paylaşın." },
        ],
        shot: "dashboard",
    },
    "bildirimler": {
        problem: "Vadesi gelen tahsilat ya da biten ürün, siz bakmadıkça fark edilmiyor.",
        solution: "Önemli olaylar için e-posta ve pano bildirimleri sizi bulsun.",
        features: [
            { title: "Vade hatırlatma", text: "Yaklaşan ve geciken vadeler için hatırlatma gelir." },
            { title: "Stok uyarısı", text: "Minimum stok seviyesine inen ürünler bildirilir." },
            { title: "Onay bildirimi", text: "Onay bekleyen belgeler ilgili kişiye iletilir." },
            { title: "Tercihlerinizi seçin", text: "Hangi olay için hangi kanaldan haber almak istediğinizi siz belirlersiniz." },
        ],
        shot: "due",
    },
    "pazaryeri": {
        problem: "Birden çok pazaryerinde satış yapınca stok ve siparişleri elle eşitlemek hata getiriyor.",
        solution: "Pazaryeri siparişleri ve stokları ERP ile senkron çalışır.",
        features: [
            { title: "Ürün eşleştirme", text: "ERP ürününü kanaldaki ürünle eşleştirin." },
            { title: "Ortak stok", text: "Satılabilir stok değiştikçe kanala gönderilir; güvenlik eşiği tanımlayabilirsiniz." },
            { title: "Sipariş aktarımı", text: "Kanaldan gelen sipariş ERP'de satış siparişi olarak açılır." },
            { title: "Hakediş mutabakatı", text: "Pazaryerinin ödemelerini siparişlerle karşılaştırın." },
        ],
        shot: "stock",
    },
    "kargo": {
        problem: "Her gönderi için ayrı kargo ekranına girip adresi yeniden yazmak zaman kaybı.",
        solution: "Sevkten tek tıkla kargo kaydı açın, etiketi alın, gönderiyi takip edin.",
        features: [
            { title: "Tek tıkla gönderi", text: "ERP sevkinden kargo kaydı açılır, etiket PDF olarak gelir." },
            { title: "Gönderi takibi", text: "Kargonun durumu siparişin üzerinde güncellenir." },
            { title: "Fiyat karşılaştırma", text: "Desi ve ağırlığa göre firmaların fiyatlarını karşılaştırın." },
            { title: "Kapıda ödeme", text: "Kapıda tahsil edilen tutarı kargo hakedişiyle eşleştirin." },
        ],
        shot: "stock",
    },
    "crm": {
        problem: "Müşteri görüşmeleri telefonlarda ve mesajlarda dağılıyor; takip edilmeyen fırsatlar kayboluyor.",
        solution: "Potansiyel müşteriden teklife kadar her görüşme ve görev tek yerde.",
        features: [
            { title: "Potansiyel müşteri ve fırsat", text: "Adayları ve satış fırsatlarını aşamalarıyla izleyin." },
            { title: "Fırsattan teklife", text: "Fırsatı tek tıkla teklife dönüştürün." },
            { title: "Görüşme geçmişi", text: "Telefon, toplantı ve ziyaret kayıtları cariye bağlanır." },
            { title: "Görev ve hatırlatma", text: "Sorumlusu ve tarihi olan görevler için hatırlatma gelir." },
        ],
        shot: "cari",
    },
    "bayi-portali": {
        problem: "Bayileriniz sipariş ve bakiye bilgisi için sizi arıyor, siz de listeleri elle gönderiyorsunuz.",
        solution: "Bayiler kendi girişleriyle sipariş verir, bakiyesini ve belgelerini görür.",
        features: [
            { title: "Bayi girişi", text: "Her bayi kendi hesabıyla giriş yapar." },
            { title: "Bayiye özel katalog", text: "Bayinin göreceği ürün ve fiyatlar sizin kurallarınızla belirlenir." },
            { title: "Sipariş ve takip", text: "Sipariş verir; sevk, kargo ve faturaları izler." },
            { title: "Hesap ekranı", text: "Açık bakiye, ekstre ve vadesi gelen taksitler bayiye görünür." },
        ],
        shot: "due",
    },
    "insan-kaynaklari": {
        problem: "Personel bilgileri, izinler ve belgeler farklı dosyalarda tutuluyor.",
        solution: "Çalışan kartı, izin ve puantaj tek bir kayıt düzeninde.",
        features: [
            { title: "Çalışan kartı", text: "Kişisel bilgiler ve özlük belgeleri bir arada." },
            { title: "İzin yönetimi", text: "İzin talebi, onay ve kalan hak takibi." },
            { title: "Vardiya ve puantaj", text: "Çalışma saatleri ve fazla mesai kayıt altına alınır." },
            { title: "Çalışan self-servis", text: "Çalışan izin ve taleplerini kendisi görür ve iletir." },
        ],
        shot: "cari",
    },
    "sabit-kiymet": {
        problem: "Demirbaşların kimde olduğu ve değerinin ne kadar düştüğü kayıtlarda kayboluyor.",
        solution: "Demirbaş kartı, zimmet ve amortisman bir arada.",
        features: [
            { title: "Demirbaş kartı", text: "Her varlığın bilgisi, değeri ve konumu kayıtlıdır." },
            { title: "Zimmet", text: "Varlığın hangi çalışanda olduğu takip edilir." },
            { title: "Amortisman", text: "Amortisman hesabı kendiliğinden yapılır." },
            { title: "Periyodik bakım", text: "Bakım tarihleri için hatırlatma alın, arızaları kaydedin." },
        ],
        shot: "stock",
    },
    "teknik-servis": {
        problem: "Servis talepleri not kağıtlarında ve mesajlarda; müşteri durumu sormak için arıyor.",
        solution: "Servis kaydını açın, teknisyene atayın, müşteriyi durum değiştikçe bilgilendirin.",
        features: [
            { title: "Servis talebi", text: "Cihaz, arıza ve garanti durumu kayda girer." },
            { title: "Teknisyen ataması", text: "Teknisyen günlük iş listesini mobil uyumlu ekrandan görür." },
            { title: "Parça kullanımı", text: "Kullanılan parçalar stoktan düşer, faturaya işler." },
            { title: "Müşteri bilgilendirme", text: "Durum değiştikçe müşteriye SMS veya e-posta gider." },
        ],
        shot: "cari",
    },
    "uretim": {
        problem: "Bir mamul için hangi malzemeden ne kadar gerektiği ve gerçek maliyeti elde bulunmuyor.",
        solution: "Reçete, iş emri ve malzeme ihtiyacı tek akışta ilerler.",
        features: [
            { title: "Ürün ağacı (reçete)", text: "Mamulün malzemelerini ve miktarlarını tanımlayın." },
            { title: "İş emri", text: "Satış siparişinden veya stoktan iş emri açın, üretimi izleyin." },
            { title: "Malzeme ihtiyaç planı", text: "Eksik malzemeler için satın alma önerisi alın." },
            { title: "Fiilî maliyet", text: "Gerçekleşen malzeme ve işçilikle üretim maliyetini görün." },
        ],
        shot: "stock",
    },
    "depo-yonetimi": {
        problem: "Depo büyüdükçe ürünü bulmak ve sayım yapmak zorlaşıyor.",
        solution: "Raf adresleri, barkodlu kabul ve toplama listeleriyle depo düzeni.",
        features: [
            { title: "Raf adresleri", text: "Her ürünün hangi rafta olduğunu bilirsiniz." },
            { title: "Barkodlu mal kabul", text: "Gelen malı barkodla okutup kabul edin." },
            { title: "Toplama ve paketleme", text: "Sipariş toplama listesi ve paketleme kontrolü." },
            { title: "Döngüsel sayım", text: "Depoyu kapatmadan adres bazında sayım yapın." },
        ],
        shot: "stock",
    },
    "kalite": {
        problem: "Gelen malın kalitesi ve sorunlu partiler kayıt altında değil, tekrar eden hatalar fark edilmiyor.",
        solution: "Kontrol planları, uygunsuzluk kayıtları ve parti izlenebilirliği.",
        features: [
            { title: "Kontrol planı", text: "Ürün veya kategori bazında kontrol adımlarını tanımlayın." },
            { title: "Numune ve AQL", text: "Sabit adet, yüzde veya AQL tablosuyla numune alın." },
            { title: "Uygunsuzluk ve DÖF", text: "Sorunları kaydedin, düzeltici faaliyeti takip edin." },
            { title: "Parti izlenebilirliği", text: "Bir partinin nereden gelip kime gittiğini görün." },
        ],
        shot: "stock",
    },
    "proje": {
        problem: "Proje bazlı işlerde hangi masrafın hangi projeye ait olduğu ay sonunda zor çıkıyor.",
        solution: "Görev, maliyet ve zaman kayıtları projeye bağlanır.",
        features: [
            { title: "Proje kartı", text: "Cari, sözleşme bedeli, tarihler ve yönetici tek kartta." },
            { title: "İş paketleri", text: "Projeyi iş paketlerine bölüp ilerlemeyi izleyin." },
            { title: "Maliyet bağlama", text: "Satın alma ve masraflar projeye bağlanır." },
            { title: "Aşamalı faturalama", text: "Kilometre taşı yüzdelerine göre fatura kesin." },
        ],
        shot: "dashboard",
    },
};
