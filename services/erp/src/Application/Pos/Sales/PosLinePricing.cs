namespace ERP.Application.Pos.Sales;

/// <summary>
///     KDV dahil fiyatlı satırın çözümü. Brüt (raf) tutar doğruluk kaynağıdır: satır brütü = fiyat x miktar (bir kez yuvarlanır), kampanya ve elle iskonto
///     brüt tarafta uygulanır, KDV brütten türetilir (net = brüt - KDV). Net birim fiyat 6 haneye yukarı yuvarlanır ve aradaki kuruş farkı iskonto olarak
///     yazılır; böylece fatura satırı (birim fiyat x miktar - iskonto) aynı neti, açık KDV tutarıyla birlikte aynı toplamı verir.
/// </summary>
/// <param name="UnitNet">Net birim fiyat (6 hane, yukarı yuvarlanmış).</param>
/// <param name="Discount">Elle iskonto (KDV hariç).</param>
/// <param name="CampaignDiscount">Kampanya iskontosu (KDV hariç).</param>
/// <param name="ManualGross">Elle iskonto (KDV dahil); iskonto politikası bu orana uygulanır.</param>
/// <param name="GrossBeforeDiscount">Raf tutarı: fiyat x miktar (KDV dahil).</param>
public readonly record struct PricedGrossLine(
    decimal UnitNet,
    decimal Discount,
    decimal CampaignDiscount,
    decimal ManualGross,
    decimal GrossBeforeDiscount,
    decimal Net,
    decimal Vat,
    decimal Total
);

public static class PosLinePricing {
    /// <param name="campaignGross">Kampanya tutarı (fiyatla aynı cins: KDV dahil).</param>
    /// <param name="manualPercent">Elle iskonto yüzdesi; kampanyadan kalan brüt tutara uygulanır.</param>
    public static PricedGrossLine VatIncluded(decimal price, decimal quantity, int vatRate, decimal campaignGross, decimal? manualPercent) {
        var shelf    = Round(quantity * price);
        var campaign = Math.Min(Round(campaignGross), shelf);
        var manual   = manualPercent is { } pct and > 0m ? Round((shelf - campaign) * pct / 100m) : 0m;

        var total = shelf - campaign - manual;
        var vat   = Round(total * vatRate / (100m + vatRate));
        var net   = total - vat;

        // Fatura satırı iskontosuz net tutardan kurulur: birim fiyat x miktar - iskonto = net. Fark (gerçek iskonto + olası mikro yuvarlama) iskonto yazılır.
        var shelfNet = shelf - Round(shelf * vatRate / (100m + vatRate));
        var unitNet  = quantity > 0m ? Math.Ceiling(shelfNet / quantity * 1_000_000m) / 1_000_000m : 0m;
        var invoiced = Round(quantity * unitNet);
        var discount = invoiced - net;
        var campaignNet = Math.Min(Round(campaign / (1m + vatRate / 100m)), discount);
        return new PricedGrossLine(unitNet, discount - campaignNet, campaignNet, manual, shelf, net, vat, total);
    }

    private static decimal Round(decimal value) {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
