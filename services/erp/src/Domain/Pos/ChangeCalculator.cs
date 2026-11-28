namespace ERP.Domain.Pos;

public static class ChangeCalculator {
    /// <summary>
    ///     Para üstü ve kasaya net giren nakit. Kalan = toplam - veresiye. Kart kalanı aşamaz; nakit (kalan - kart)
    ///     tutarından az olamaz. Nakit fazlası para üstü olarak geri verilir.
    /// </summary>
    public static (decimal Change, decimal CashNet) Compute(decimal total, decimal card, decimal cash, decimal onAccount) {
        if (total < 0 || card < 0 || cash < 0 || onAccount < 0)
            throw new PosRuleException("payment_insufficient", "Tutarlar negatif olamaz.");
        var remaining = total - onAccount;
        if (remaining < 0)
            throw new PosRuleException("payment_insufficient", "Veresiye tutarı toplamı aşamaz.");
        if (card > remaining)
            throw new PosRuleException("card_exceeds_remaining", "Kart tutarı kalan tutarı aşamaz.");
        var cashDue = remaining - card;
        if (cash < cashDue)
            throw new PosRuleException("payment_insufficient", "Ödeme toplam tutarı karşılamıyor.");
        return (cash - cashDue, cashDue);
    }
}
