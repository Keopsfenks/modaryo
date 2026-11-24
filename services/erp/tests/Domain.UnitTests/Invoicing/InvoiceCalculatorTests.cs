using ERP.Domain.Invoicing;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Invoicing;

public class InvoiceCalculatorTests {
    private static CalcLine Line(decimal qty, decimal price, int vat = 20, bool vatIncluded = false,
                                 decimal? discPct = null, decimal? discAmt = null,
                                 decimal? otvPct = null, decimal? otvUnit = null,
                                 int? withholding = null, bool exempt = false) =>
        new(qty, price, vatIncluded, discPct, discAmt, vat, otvPct, otvUnit, withholding, exempt);

    [Test]
    public void Calculator_SingleLineVat20() {
        var r = InvoiceCalculator.Calculate([Line(2, 100m)], null, null);

        r.Gross.ShouldBe(200m);
        r.Discount.ShouldBe(0m);
        r.Net.ShouldBe(200m);
        r.Otv.ShouldBe(0m);
        r.Vat.ShouldBe(40m);
        r.Withheld.ShouldBe(0m);
        r.Payable.ShouldBe(240m);
        r.Lines[0].Total.ShouldBe(240m);
    }

    [Test]
    public void Calculator_VatIncludedPriceConvertsToNet() {
        // 120 KDV dahil, %20 -> net 100.
        var a = InvoiceCalculator.Calculate([Line(1, 120m, 20, vatIncluded: true)], null, null);
        a.Net.ShouldBe(100m);
        a.Vat.ShouldBe(20m);
        a.Payable.ShouldBe(120m);

        // 110 KDV dahil, %10 -> net 100.
        var b = InvoiceCalculator.Calculate([Line(1, 110m, 10, vatIncluded: true)], null, null);
        b.Net.ShouldBe(100m);
        b.Vat.ShouldBe(10m);

        // 99.99 / 1.2 = 83.325 (6 hane: 83.325000); x3 = 249.975 -> 249.98 (sıfırdan uzağa).
        // KDV 249.98 x 0.20 = 49.996 -> 50.00; ödenecek 299.98.
        var c = InvoiceCalculator.Calculate([Line(3, 99.99m, 20, vatIncluded: true)], null, null);
        c.Gross.ShouldBe(249.98m);
        c.Net.ShouldBe(249.98m);
        c.Vat.ShouldBe(50.00m);
        c.Payable.ShouldBe(299.98m);
    }

    [Test]
    public void Calculator_VatIncludedUnitPriceRoundedTo6Decimals() {
        // 10 / 1.2 = 8.3333333... -> 8.333333; x 3 = 24.999999 -> 25.00
        var r = InvoiceCalculator.Calculate([Line(3, 10m, 20, vatIncluded: true)], null, null);
        r.Gross.ShouldBe(25.00m);
    }

    [Test]
    public void Calculator_LinePercentAndAmountDiscount() {
        // %10 indirim: 200 -> 20.
        var p = InvoiceCalculator.Calculate([Line(2, 100m, discPct: 10m)], null, null);
        p.Lines[0].LineDiscount.ShouldBe(20m);
        p.Net.ShouldBe(180m);
        p.Vat.ShouldBe(36m);
        p.Discount.ShouldBe(20m);

        // Tutar indirimi 15.50: net 184.50, KDV 36.90.
        var a = InvoiceCalculator.Calculate([Line(2, 100m, discAmt: 15.5m)], null, null);
        a.Lines[0].LineDiscount.ShouldBe(15.5m);
        a.Net.ShouldBe(184.5m);
        a.Vat.ShouldBe(36.9m);
        a.Payable.ShouldBe(221.4m);

        // 33.33 x 3 = 99.99; %12.5 = 12.49875 -> 12.50
        var r = InvoiceCalculator.Calculate([Line(3, 33.33m, discPct: 12.5m)], null, null);
        r.Lines[0].LineDiscount.ShouldBe(12.50m);
        r.Net.ShouldBe(87.49m);
    }

    [Test]
    public void Calculator_LineDiscountBothPercentAndAmount_Throws() {
        Should.Throw<ArgumentException>(() => InvoiceCalculator.Calculate([Line(1, 100m, discPct: 5m, discAmt: 5m)], null, null));
    }

    [TestCase("-1")]
    [TestCase("100.01")]
    [TestCase("10.123")]
    public void Calculator_InvalidDiscountPercent_Throws(string pct) {
        var p = decimal.Parse(pct, System.Globalization.CultureInfo.InvariantCulture);
        Should.Throw<ArgumentException>(() => InvoiceCalculator.Calculate([Line(1, 100m, discPct: p)], null, null));
    }

    [Test]
    public void Calculator_DiscountAboveGross_Throws() {
        Should.Throw<ArgumentException>(() => InvoiceCalculator.Calculate([Line(1, 100m, discAmt: 100.01m)], null, null));
    }

    [Test]
    public void Calculator_DocDiscountMixedVat_ReconcilesToTheKurus() {
        // Satırlar (KDV hariç):
        //   L1: 3 x 33.33 = 99.99   KDV %20
        //   L2: 7 x 14.29 = 100.03  KDV %10
        //   L3: 11 x 9.07 = 99.77   KDV %1
        //   Toplam 299.79. Belge indirimi %7 = 20.9853 -> 20.99.
        // Dağıtım (kuruş cinsinden 2099 x net / 29979):
        //   L1 700.0868 -> 700 ; L2 700.3668 -> 700 ; L3 698.5464 -> 698
        //   Tabanlar toplamı 2098, kalan 1 kuruş en büyük kalan (L3, .5464) -> L3 699.
        //   Dağıtılan: 7.00 / 7.00 / 6.99 = 20.99
        // Net: 92.99 / 93.03 / 92.78 = 278.80
        // KDV: 92.99x0.20=18.598->18.60 ; 93.03x0.10=9.303->9.30 ; 92.78x0.01=0.9278->0.93 ; toplam 28.83
        // Ödenecek: 278.80 + 28.83 = 307.63
        var r = InvoiceCalculator.Calculate([Line(3, 33.33m, 20), Line(7, 14.29m, 10), Line(11, 9.07m, 1)], 7m, null);

        r.Lines.Select(l => l.Gross).ShouldBe([99.99m, 100.03m, 99.77m]);
        r.Lines.Select(l => l.DocDiscount).ShouldBe([7.00m, 7.00m, 6.99m]);
        r.Lines.Select(l => l.Net).ShouldBe([92.99m, 93.03m, 92.78m]);
        r.Lines.Select(l => l.Vat).ShouldBe([18.60m, 9.30m, 0.93m]);

        r.Gross.ShouldBe(299.79m);
        r.Discount.ShouldBe(20.99m);
        r.Net.ShouldBe(278.80m);
        r.Vat.ShouldBe(28.83m);
        r.Payable.ShouldBe(307.63m);

        // Mutabakat: toplamlar satırların toplamıdır, ödenecek parçaların toplamıdır.
        r.Net.ShouldBe(r.Lines.Sum(l => l.Net));
        r.Vat.ShouldBe(r.Lines.Sum(l => l.Vat));
        r.Discount.ShouldBe(r.Lines.Sum(l => l.LineDiscount + l.DocDiscount));
        r.Payable.ShouldBe(r.Net + r.Otv + r.Vat - r.Withheld);
        r.Payable.ShouldBe(r.Lines.Sum(l => l.Total));
        r.VatSummary.Sum(s => s.Vat).ShouldBe(r.Vat);
        r.VatSummary.Sum(s => s.Base).ShouldBe(r.Net + r.Otv);
    }

    [Test]
    public void Calculator_DocDiscountAmount_AllocatesExactly() {
        // 3 eşit satır 100.00, belge indirimi 10.00: payların her biri 3.333.. -> 3.33, 3.33, 3.33, kalan 1 kuruş ilk satıra (eşit kalanlarda ilk).
        var r = InvoiceCalculator.Calculate([Line(1, 100m), Line(1, 100m), Line(1, 100m)], null, 10m);
        r.Lines.Select(l => l.DocDiscount).ShouldBe([3.34m, 3.33m, 3.33m]);
        r.Lines.Sum(l => l.DocDiscount).ShouldBe(10m);
        r.Net.ShouldBe(290m);
        r.Discount.ShouldBe(10m);
    }

    [Test]
    public void Calculator_DocDiscountAfterLineDiscount() {
        // L1 100 (%10 satır indirimi -> 90), L2 100 -> toplam 190; belge indirimi 19.00 -> 9.00 / 10.00
        var r = InvoiceCalculator.Calculate([Line(1, 100m, discPct: 10m), Line(1, 100m)], null, 19m);
        r.Lines.Select(l => l.DocDiscount).ShouldBe([9.00m, 10.00m]);
        r.Net.ShouldBe(171m);
        r.Discount.ShouldBe(10m + 19m);
    }

    [Test]
    public void Calculator_DocDiscountPercentAndAmount_Throws() {
        Should.Throw<ArgumentException>(() => InvoiceCalculator.Calculate([Line(1, 100m)], 5m, 5m));
    }

    [Test]
    public void Calculator_DocDiscountAboveNet_Throws() {
        Should.Throw<ArgumentException>(() => InvoiceCalculator.Calculate([Line(1, 100m)], null, 100.01m));
    }

    [Test]
    public void Calculator_Withholding9Tenths() {
        // Net 1000, KDV %20 = 200, tevkifat 9/10 = 180; ödenecek 1000 + 200 - 180 = 1020.
        var r = InvoiceCalculator.Calculate([Line(1, 1000m, withholding: 9)], null, null);
        r.Vat.ShouldBe(200m);
        r.Withheld.ShouldBe(180m);
        r.Payable.ShouldBe(1020m);
        r.Lines[0].Total.ShouldBe(1020m);

        // 5/10: KDV 0.93 x 5/10 = 0.465 -> 0.47
        var s = InvoiceCalculator.Calculate([Line(1, 92.78m, 1, withholding: 5)], null, null);
        s.Vat.ShouldBe(0.93m);
        s.Withheld.ShouldBe(0.47m);
        s.Payable.ShouldBe(92.78m + 0.93m - 0.47m);
    }

    [Test]
    public void Calculator_OtvIncreasesVatBase() {
        // Net 1000, ÖTV %10 = 100, KDV matrahı 1100, KDV %20 = 220, ödenecek 1320.
        var r = InvoiceCalculator.Calculate([Line(1, 1000m, otvPct: 10m)], null, null);
        r.Otv.ShouldBe(100m);
        r.Lines[0].VatBase.ShouldBe(1100m);
        r.Vat.ShouldBe(220m);
        r.Payable.ShouldBe(1320m);

        // Birim başına ÖTV: 4 adet x 2.50 = 10; matrah 400 + 10 = 410; KDV 82; ödenecek 492.
        var u = InvoiceCalculator.Calculate([Line(4, 100m, otvUnit: 2.5m)], null, null);
        u.Otv.ShouldBe(10m);
        u.Lines[0].VatBase.ShouldBe(410m);
        u.Vat.ShouldBe(82m);
        u.Payable.ShouldBe(492m);
    }

    [Test]
    public void Calculator_OtvBothPercentAndUnit_Throws() {
        Should.Throw<ArgumentException>(() => InvoiceCalculator.Calculate([Line(1, 100m, otvPct: 5m, otvUnit: 1m)], null, null));
    }

    [Test]
    public void Calculator_OtvIsOnNetAfterDocDiscount() {
        // 1000 net, belge indirimi %10 -> 900; ÖTV %10 = 90; matrah 990; KDV 198.
        var r = InvoiceCalculator.Calculate([Line(1, 1000m, otvPct: 10m)], 10m, null);
        r.Net.ShouldBe(900m);
        r.Otv.ShouldBe(90m);
        r.Vat.ShouldBe(198m);
        r.Payable.ShouldBe(1188m);
    }

    [Test]
    public void Calculator_ExemptLineZeroVat() {
        var r = InvoiceCalculator.Calculate([Line(5, 40m, 0, exempt: true)], null, null);
        r.Net.ShouldBe(200m);
        r.Vat.ShouldBe(0m);
        r.Payable.ShouldBe(200m);
    }

    [Test]
    public void Calculator_ExemptWithNonZeroVat_Throws() {
        Should.Throw<ArgumentException>(() => InvoiceCalculator.Calculate([Line(1, 100m, 20, exempt: true)], null, null));
    }

    [Test]
    public void Calculator_ExemptWithWithholding_Throws() {
        Should.Throw<ArgumentException>(() => InvoiceCalculator.Calculate([Line(1, 100m, 0, withholding: 5, exempt: true)], null, null));
    }

    [Test]
    public void Calculator_InvalidVatRate_Throws() {
        Should.Throw<ArgumentException>(() => InvoiceCalculator.Calculate([Line(1, 100m, 18)], null, null));
    }

    [Test]
    public void Calculator_VatSummaryGroupsByRate() {
        var r = InvoiceCalculator.Calculate([Line(1, 100m, 20), Line(1, 50m, 10), Line(1, 200m, 20), Line(1, 10m, 0)], null, null);

        r.VatSummary.Count.ShouldBe(3);
        r.VatSummary[0].ShouldBe((0, 10m, 0m));
        r.VatSummary[1].ShouldBe((10, 50m, 5m));
        r.VatSummary[2].ShouldBe((20, 300m, 60m));
        r.Vat.ShouldBe(65m);
        r.Payable.ShouldBe(360m + 65m);
    }

    [Test]
    public void Calculator_HalfKurusRoundsAwayFromZero() {
        // 0.25 x 0.10 -> 0.025 net? Net 0.05 (1 x 0.05), KDV %10 = 0.005 -> 0.01
        var r = InvoiceCalculator.Calculate([Line(1, 0.05m, 10)], null, null);
        r.Vat.ShouldBe(0.01m);
    }

    [Test]
    public void Calculator_EmptyLines_ZeroTotals() {
        var r = InvoiceCalculator.Calculate([], null, null);
        r.Payable.ShouldBe(0m);
        r.VatSummary.ShouldBeEmpty();
    }

    [Test]
    public void Calculator_RandomizedInvoices_AlwaysReconcile() {
        var rnd = new Random(42);
        int[] rates = [0, 1, 10, 20];
        for (var i = 0; i < 300; i++) {
            var lines = new List<CalcLine>();
            for (var j = 0; j < rnd.Next(1, 8); j++) {
                var vat = rates[rnd.Next(rates.Length)];
                lines.Add(Line(rnd.Next(1, 20), Math.Round((decimal)rnd.NextDouble() * 500m + 0.01m, 2), vat,
                               vatIncluded: rnd.Next(2) == 0, discPct: rnd.Next(3) == 0 ? rnd.Next(0, 30) : null,
                               withholding: vat > 0 && rnd.Next(4) == 0 ? rnd.Next(1, 10) : null, exempt: false));
            }
            var pct = rnd.Next(3) == 0 ? rnd.Next(1, 40) : (decimal?)null;
            var r   = InvoiceCalculator.Calculate(lines, pct, null);

            r.Net.ShouldBe(r.Lines.Sum(l => l.Net));
            r.Payable.ShouldBe(r.Net + r.Otv + r.Vat - r.Withheld);
            r.Payable.ShouldBe(r.Lines.Sum(l => l.Total));
            r.Lines.Sum(l => l.Gross - l.LineDiscount - l.DocDiscount).ShouldBe(r.Net);
            if (pct is not null) {
                var net1 = r.Lines.Sum(l => l.Gross - l.LineDiscount);
                r.Lines.Sum(l => l.DocDiscount).ShouldBe(Math.Round(net1 * pct.Value / 100m, 2, MidpointRounding.AwayFromZero));
            }
        }
    }
}
