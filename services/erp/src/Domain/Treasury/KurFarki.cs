namespace ERP.Domain.Treasury;

public static class KurFarki {
    /// <summary>Tahsis edilen döviz tutarı × (alacak kuru − borç kuru), 2 haneye yuvarlanır. Alacak tarafı için pozitif = kur farkı geliri.</summary>
    public static decimal Compute(decimal allocatedForeign, decimal debitRate, decimal creditRate) =>
        Math.Round(allocatedForeign * (creditRate - debitRate), 2, MidpointRounding.AwayFromZero);
}
