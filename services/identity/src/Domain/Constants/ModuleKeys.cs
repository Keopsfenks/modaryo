namespace IdentityService.Domain.Constants;

/// <summary>Firma bazında açılıp kapatılabilen modüller (tek kaynak).</summary>
public static class ModuleKeys
{
    public sealed record ModuleInfo(string Key, string Name, string Description, int Wave);

    public static IReadOnlyList<ModuleInfo> All { get; } =
    [
        new("pos", "Perakende Satış (POS)", "Mağaza kasasında hızlı satış, tahsilat ve vardiya yönetimi sağlar.", 1),
        new("cek-senet", "Çek ve Senet", "Alınan ve verilen çek ve senetlerin portföy, tahsilat ve ciro takibini yapar.", 1),
        new("ithalat", "İthalat", "Yurt dışı alımlarda gümrük, masraf dağıtımı ve döviz maliyetini yönetir.", 1),
        new("siparise-gore-tedarik", "Siparişe Göre Tedarik", "Müşteri siparişlerini tedarikçi siparişlerine bağlayarak temin sürecini izler.", 1),
        new("teslimat-plani", "Teslimat Planı", "Sevkiyatları araç, rota ve gün bazında planlar.", 1),
        new("seri-lot", "Seri ve Lot Takibi", "Stok hareketlerini seri numarası ve lot bazında izlenebilir kılar.", 1),
        new("sube", "Şubeler", "Birden fazla şube tanımlamayı ve yetkileri şube bazında kısıtlamayı sağlar.", 1),
        new("crm", "Müşteri İlişkileri (CRM)", "Fırsat, görüşme ve müşteri iletişim geçmişini yönetir.", 2),
        new("pazaryeri", "Pazaryeri Entegrasyonları", "Pazaryerlerindeki ürün, stok ve siparişleri senkronize eder.", 2),
        new("kargo", "Kargo Entegrasyonları", "Kargo firmalarıyla gönderi oluşturma ve takip işlemlerini otomatikleştirir.", 2),
        new("b2b", "Bayi Portalı (B2B)", "Bayilerin kendi fiyatlarıyla sipariş verebildiği portal sunar.", 2),
        new("ik", "İnsan Kaynakları", "Personel, izin ve bordro süreçlerini yönetir.", 2),
        new("sabit-kiymet", "Sabit Kıymetler", "Demirbaşların kayıt, amortisman ve elden çıkarma işlemlerini izler.", 2),
        new("teknik-servis", "Teknik Servis", "Servis kayıtlarını, parça kullanımını ve garanti süreçlerini yönetir.", 2),
        new("mrp", "Üretim Planlama (MRP)", "Ürün ağaçlarına göre malzeme ihtiyaç planı ve iş emri üretir.", 2),
        new("wms", "Depo Yönetimi (WMS)", "Depo içi raf, toplama ve yerleştirme operasyonlarını yönetir.", 2),
        new("kalite", "Kalite Kontrol", "Giriş, üretim ve çıkış kalite kontrollerini kayıt altına alır.", 2),
        new("proje", "Proje Yönetimi", "Proje bazlı görev, maliyet ve karlılık takibi sağlar.", 2),
    ];

    private static readonly HashSet<string> Known = All.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string key) => Known.Contains(key);
}
