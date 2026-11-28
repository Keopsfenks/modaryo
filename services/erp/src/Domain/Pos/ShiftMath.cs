namespace ERP.Domain.Pos;

public static class ShiftMath {
    /// <summary>Beklenen nakit = açılış + net nakit satış (para üstü düşülmüş) - nakit iade + kasa girişi - kasa çıkışı.</summary>
    public static decimal ExpectedCash(decimal opening, decimal cashSalesNet, decimal cashRefunds, decimal cashIn, decimal cashOut) {
        return opening + cashSalesNet - cashRefunds + cashIn - cashOut;
    }
}
