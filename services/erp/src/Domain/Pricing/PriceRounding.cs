namespace ERP.Domain.Pricing;

public static class PriceRounding {
    /// <summary>Türetilmiş değerlere uygulanır. NineEnding: en yakın x9,90 (eşitlikte yukarı).</summary>
    public static decimal Apply(decimal value, RoundingRule rule) {
        return rule switch {
            RoundingRule.None       => value,
            RoundingRule.Cent       => ToStep(value, 0.01m),
            RoundingRule.FiveCents  => ToStep(value, 0.05m),
            RoundingRule.TenCents   => ToStep(value, 0.10m),
            RoundingRule.FiftyCents => ToStep(value, 0.50m),
            RoundingRule.One        => ToStep(value, 1m),
            RoundingRule.NineEnding => NineEnding(value),
            _                       => value
        };
    }

    static decimal ToStep(decimal value, decimal step) {
        return Math.Round(value / step, 0, MidpointRounding.AwayFromZero) * step;
    }

    static decimal NineEnding(decimal value) {
        var k      = Math.Round((value - 9.90m) / 10m, 0, MidpointRounding.AwayFromZero);
        var result = k * 10m + 9.90m;
        return result < 9.90m ? 9.90m : result;
    }
}
