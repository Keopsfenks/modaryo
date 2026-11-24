namespace ERP.Domain.Invoicing;

public sealed record CalcLine(
    decimal  Quantity,
    decimal  UnitPrice,
    bool     VatIncluded,
    decimal? DiscountPercent,
    decimal? DiscountAmount,
    int      VatRate,
    decimal? OtvPercent,
    decimal? OtvPerUnit,
    int?     WithholdingTenths,
    bool     Exempt);

public sealed record CalcLineResult(
    decimal Gross,
    decimal LineDiscount,
    decimal DocDiscount,
    decimal Net,
    decimal Otv,
    decimal VatBase,
    decimal Vat,
    decimal Withheld,
    decimal Total);

public sealed record CalcResult(
    IReadOnlyList<CalcLineResult>                        Lines,
    decimal                                              Gross,
    decimal                                              Discount,
    decimal                                              Net,
    decimal                                              Otv,
    decimal                                              Vat,
    decimal                                              Withheld,
    decimal                                              Payable,
    IReadOnlyList<(int Rate, decimal Base, decimal Vat)> VatSummary);

/// <summary>
///     Fatura hesaplayıcısı. Tüm hesap decimal; yuvarlama MidpointRounding.AwayFromZero.
///     Satır brüt tutarı 2 basamağa yuvarlanır; belge toplamları satır değerlerinin toplamıdır (yeniden yuvarlama yok).
/// </summary>
public static class InvoiceCalculator {
    public static readonly int[] AllowedVatRates = [0, 1, 10, 20];

    public static CalcResult Calculate(IReadOnlyList<CalcLine> lines, decimal? docDiscountPercent, decimal? docDiscountAmount) {
        if (docDiscountPercent is not null && docDiscountAmount is not null) {
            throw new ArgumentException("Belge indirimi yüzde ve tutar olarak birlikte verilemez.");
        }

        if (docDiscountPercent is { } dp) {
            ValidatePercent(dp, "Belge indirim yüzdesi");
        }

        if (docDiscountAmount is < 0) {
            throw new ArgumentException("Belge indirim tutarı negatif olamaz.");
        }

        // 1) Satır brüt, satır indirimi, indirim sonrası net.
        var gross        = new decimal[lines.Count];
        var lineDiscount = new decimal[lines.Count];
        var afterLine    = new decimal[lines.Count];
        for (var i = 0; i < lines.Count; i++) {
            var l = lines[i];
            Validate(l);
            var unit = l.VatIncluded ? Round(l.UnitPrice / (1m + l.VatRate / 100m), 6) : l.UnitPrice;
            gross[i] = Round(l.Quantity * unit, 2);

            lineDiscount[i] = l.DiscountPercent is { } pct ? Round(gross[i] * pct / 100m, 2) :
                              l.DiscountAmount is { } amt  ? Round(amt, 2) : 0m;
            if (lineDiscount[i] > gross[i]) {
                throw new ArgumentException("Satır indirimi satır tutarını aşamaz.");
            }

            afterLine[i] = gross[i] - lineDiscount[i];
        }

        // 2) Belge indirimi: toplam tutar ve en büyük kalan yöntemiyle dağıtım.
        var sumAfterLine = afterLine.Sum();
        var docTotal = docDiscountPercent is { } p ? Round(sumAfterLine * p / 100m, 2) :
                       docDiscountAmount is { } a  ? Round(a, 2) : 0m;
        if (docTotal > sumAfterLine) {
            throw new ArgumentException("Belge indirimi toplam tutarı aşamaz.");
        }

        var docShare = Allocate(docTotal, afterLine);

        // 3) Net, ÖTV, KDV, tevkifat.
        var results = new List<CalcLineResult>(lines.Count);
        for (var i = 0; i < lines.Count; i++) {
            var l   = lines[i];
            var net = afterLine[i] - docShare[i];
            var otv = l.OtvPercent is { } op ? Round(net * op / 100m, 2) :
                      l.OtvPerUnit is { } ou ? Round(l.Quantity * ou, 2) : 0m;
            var vatBase  = net + otv;
            var vat      = Round(vatBase * l.VatRate / 100m, 2);
            var withheld = l.WithholdingTenths is { } n ? Round(vat * n / 10m, 2) : 0m;
            results.Add(new CalcLineResult(gross[i], lineDiscount[i], docShare[i], net, otv, vatBase, vat, withheld, net + otv + vat - withheld));
        }

        var summary = results.Select((r, i) => (Rate: lines[i].VatRate, r.VatBase, r.Vat))
                             .GroupBy(x => x.Rate)
                             .OrderBy(g => g.Key)
                             .Select(g => (g.Key, g.Sum(x => x.VatBase), g.Sum(x => x.Vat)))
                             .ToList();

        var totalNet      = results.Sum(r => r.Net);
        var totalOtv      = results.Sum(r => r.Otv);
        var totalVat      = results.Sum(r => r.Vat);
        var totalWithheld = results.Sum(r => r.Withheld);
        return new CalcResult(results, results.Sum(r => r.Gross), results.Sum(r => r.LineDiscount + r.DocDiscount), totalNet, totalOtv,
                              totalVat, totalWithheld, totalNet + totalOtv + totalVat - totalWithheld, summary);
    }

    /// <summary>
    ///     Toplamı ağırlıklara orantılı dağıtır (2 basamak). Kuruş tabanları alınır, kalan kuruşlar en büyük kesirli kalana
    ///     (eşitlikte ilk satıra) verilir; böylece paylar toplamı tam olarak total eder.
    /// </summary>
    internal static decimal[] Allocate(decimal total, decimal[] weights) {
        var result = new decimal[weights.Length];
        var sum    = weights.Sum();
        if (total == 0m || sum == 0m) {
            return result;
        }

        var totalCents = (long)Round(total * 100m, 0);
        var exact      = new decimal[weights.Length];
        var floors     = new long[weights.Length];
        long assigned  = 0;
        for (var i = 0; i < weights.Length; i++) {
            exact[i]  = totalCents * weights[i] / sum;
            floors[i] = (long)Math.Floor(exact[i]);
            assigned += floors[i];
        }

        var remaining = (int)(totalCents - assigned);
        foreach (var i in Enumerable.Range(0, weights.Length)
                                    .OrderByDescending(i => exact[i] - floors[i])
                                    .ThenBy(i => i)
                                    .Take(remaining)) {
            floors[i]++;
        }

        for (var i = 0; i < weights.Length; i++) {
            result[i] = floors[i] / 100m;
        }

        return result;
    }

    private static void Validate(CalcLine l) {
        if (!AllowedVatRates.Contains(l.VatRate)) {
            throw new ArgumentException($"Geçersiz KDV oranı: {l.VatRate}.");
        }

        if (l.DiscountPercent is not null && l.DiscountAmount is not null) {
            throw new ArgumentException("Satır indirimi yüzde ve tutar olarak birlikte verilemez.");
        }

        if (l.DiscountPercent is { } dp) {
            ValidatePercent(dp, "Satır indirim yüzdesi");
        }

        if (l.DiscountAmount is < 0) {
            throw new ArgumentException("Satır indirim tutarı negatif olamaz.");
        }

        if (l.OtvPercent is not null && l.OtvPerUnit is not null) {
            throw new ArgumentException("ÖTV yüzde ve birim tutar olarak birlikte verilemez.");
        }

        if (l.OtvPercent is < 0 || l.OtvPerUnit is < 0) {
            throw new ArgumentException("ÖTV negatif olamaz.");
        }

        if (l.Exempt && l.VatRate != 0) {
            throw new ArgumentException("İstisna yalnızca %0 KDV ile kullanılabilir.");
        }

        if (l.Exempt && l.WithholdingTenths is not null) {
            throw new ArgumentException("İstisnalı satırda tevkifat olamaz.");
        }

        if (l.WithholdingTenths is < 1 or > 10) {
            throw new ArgumentException("Tevkifat oranı 1/10 ile 10/10 arasında olmalıdır.");
        }

        if (l.Quantity < 0 || l.UnitPrice < 0) {
            throw new ArgumentException("Miktar ve birim fiyat negatif olamaz.");
        }
    }

    private static void ValidatePercent(decimal pct, string label) {
        if (pct < 0 || pct > 100 || Round(pct, 2) != pct) {
            throw new ArgumentException($"{label} 0-100 arasında ve en çok 2 ondalıklı olmalıdır.");
        }
    }

    private static decimal Round(decimal v, int decimals) => Math.Round(v, decimals, MidpointRounding.AwayFromZero);
}
