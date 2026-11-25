namespace ERP.Domain.Sales;

/// <summary>Taksit planı satırı (peşinat dahil değildir; peşinat için sıra 0 kullanılır).</summary>
public sealed record PlanRow(int Sequence, DateOnly DueDate, decimal Amount);

public static class InstallmentSplitter {
    /// <summary>Toplamdan peşinatı düşüp kalanı eşit böler; kuruş artığı son satıra gider.</summary>
    public static IReadOnlyList<PlanRow> Equal(decimal total, decimal downPayment, int count, DateOnly firstDue) {
        if (count is < 1 or > 36) throw new ArgumentOutOfRangeException(nameof(count), "Taksit sayısı 1-36 arasında olmalıdır.");
        if (downPayment < 0m || downPayment > total) throw new ArgumentOutOfRangeException(nameof(downPayment), "Peşinat geçersiz.");

        var rest  = decimal.Round(total - downPayment, 2);
        var each  = Math.Floor(rest / count * 100m) / 100m;
        var rows  = new List<PlanRow>(count);
        var given = 0m;
        for (var i = 0; i < count; i++) {
            var amount = i == count - 1 ? rest - given : each;
            given += amount;
            rows.Add(new PlanRow(i + 1, firstDue.AddMonths(i), amount));
        }
        return rows;
    }
}
