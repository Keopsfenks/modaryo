namespace ERP.Domain.Shipping;

public static class Desi {
    /// <summary>Hacimsel ağırlık: en x boy x yükseklik (cm) / 3000, 0,1'e yukarı yuvarlanır.</summary>
    public static decimal Of(decimal widthCm, decimal lengthCm, decimal heightCm) {
        if (widthCm <= 0m || lengthCm <= 0m || heightCm <= 0m) return 0m;
        return Math.Ceiling(widthCm * lengthCm * heightCm / 3000m * 10m) / 10m;
    }
}
