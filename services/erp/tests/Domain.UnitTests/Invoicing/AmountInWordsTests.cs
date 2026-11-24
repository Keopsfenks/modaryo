using ERP.Domain.Invoicing;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Invoicing;

public class AmountInWordsTests {
    [Test]
    public void AmountInWords_TRY_1350_50() {
        TurkishAmountInWords.Format(1350.50m, "TRY").ShouldBe("Yalnız Bin Üç Yüz Elli Türk Lirası Elli Kuruş");
    }

    [Test]
    public void AmountInWords_TRY_1300_50_MatchesSpecExample() {
        TurkishAmountInWords.Format(1300.50m, "TRY").ShouldBe("Yalnız Bin Üç Yüz Türk Lirası Elli Kuruş");
    }

    [Test]
    public void AmountInWords_Zero() {
        TurkishAmountInWords.Format(0m, "TRY").ShouldBe("Yalnız Sıfır Türk Lirası");
    }

    [Test]
    public void AmountInWords_USD() {
        TurkishAmountInWords.Format(12.05m, "USD").ShouldBe("Yalnız On İki Amerikan Doları Beş Sent");
    }

    [Test]
    public void AmountInWords_OnlyKurus() {
        TurkishAmountInWords.Format(0.75m, "TRY").ShouldBe("Yalnız Yetmiş Beş Kuruş");
    }

    [TestCase("1", "Yalnız Bir Türk Lirası")]
    [TestCase("100", "Yalnız Yüz Türk Lirası")]
    [TestCase("1000", "Yalnız Bin Türk Lirası")]
    [TestCase("2001", "Yalnız İki Bin Bir Türk Lirası")]
    [TestCase("11000", "Yalnız On Bir Bin Türk Lirası")]
    [TestCase("1000000", "Yalnız Bir Milyon Türk Lirası")]
    [TestCase("1234567.89", "Yalnız Bir Milyon İki Yüz Otuz Dört Bin Beş Yüz Altmış Yedi Türk Lirası Seksen Dokuz Kuruş")]
    [TestCase("3000000000", "Yalnız Üç Milyar Türk Lirası")]
    public void AmountInWords_Numbers(string amount, string expected) {
        TurkishAmountInWords.Format(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "TRY").ShouldBe(expected);
    }

    [Test]
    public void AmountInWords_EurAndGbp() {
        TurkishAmountInWords.Format(2m, "EUR").ShouldBe("Yalnız İki Avro");
        TurkishAmountInWords.Format(3.10m, "GBP").ShouldBe("Yalnız Üç İngiliz Sterlini On Peni");
    }

    [Test]
    public void AmountInWords_RoundsToTwoDecimalsAwayFromZero() {
        TurkishAmountInWords.Format(1.005m, "TRY").ShouldBe("Yalnız Bir Türk Lirası Bir Kuruş");
    }
}
