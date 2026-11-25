namespace ERP.Domain.Sales;

public static class PlanConsumer {
    /// <summary>
    /// Faturanın vade planını üretir: önce kalan peşinat (sıra 0, sipariş tarihi), sonra plan satırları sırayla tüketilir.
    /// Son tüketilen satır kısmen karşılanırsa bölünür; kalan kısım sonraki faturaya kalır.
    /// </summary>
    public static (IReadOnlyList<PlanRow> Schedule, IReadOnlyList<PlanRow> Remaining, decimal DownPaymentRemaining) Consume(
        IReadOnlyList<PlanRow> remaining, decimal downPaymentRemaining, decimal invoicePayable, DateOnly orderDate) {
        var schedule = new List<PlanRow>();
        var rows     = new List<PlanRow>(remaining.OrderBy(r => r.Sequence));
        var left     = decimal.Round(invoicePayable, 2);

        if (downPaymentRemaining > 0m && left > 0m) {
            var take = Math.Min(downPaymentRemaining, left);
            schedule.Add(new PlanRow(0, orderDate, take));
            downPaymentRemaining -= take;
            left                 -= take;
        }

        while (left > 0m && rows.Count > 0) {
            var row = rows[0];
            if (row.Amount <= left) {
                schedule.Add(row);
                left -= row.Amount;
                rows.RemoveAt(0);
            } else {
                schedule.Add(row with { Amount = left });
                rows[0] = row with { Amount = row.Amount - left };
                left    = 0m;
            }
        }

        if (left > 0m) throw new InvalidOperationException("Fatura tutarı kalan peşinat ve taksit toplamını aşıyor.");
        return (schedule, rows, downPaymentRemaining);
    }
}
