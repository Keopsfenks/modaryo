namespace ERP.Domain.Pricing;

public sealed record CampaignLine(
    Guid ProductId, Guid? VariantId, Guid? CategoryId, decimal Quantity, decimal UnitPrice);

public sealed record CampaignResult(Guid CampaignId, string Name, decimal DiscountAmount);

public static class CampaignEvaluator {
    /// <summary>Satır başına en iyi tek kampanya; kampanyalar birikmez.</summary>
    public static CampaignResult? Best(CampaignLine line, IEnumerable<Campaign> campaigns, DateTime istanbulNow) {
        CampaignResult? best = null;
        foreach (var c in campaigns) {
            if (!Applies(c, line, istanbulNow)) continue;
            var amount = Discount(c, line);
            if (amount <= 0m) continue;
            if (best is null || amount > best.DiscountAmount) best = new CampaignResult(c.Id, c.Name, amount);
        }

        return best;
    }

    static decimal Discount(Campaign c, CampaignLine line) {
        switch (c.Kind) {
            case CampaignKind.BuyXPayY: {
                if (c.BuyQty is not { } x || c.PayQty is not { } y || x <= y || y < 1) return 0m;
                var groups = Math.Floor(line.Quantity / x);
                return groups * (x - y) * line.UnitPrice;
            }
            case CampaignKind.PercentOff: {
                if (c.Percent is not { } p || p <= 0m || p > 100m) return 0m;
                return line.Quantity * line.UnitPrice * p / 100m;
            }
            default:
                return 0m;
        }
    }

    static bool Applies(Campaign c, CampaignLine line, DateTime now) {
        if (!c.IsActive) return false;

        var date = DateOnly.FromDateTime(now);
        if (c.ValidFrom is not null && date < c.ValidFrom) return false;
        if (c.ValidTo is not null && date > c.ValidTo) return false;

        if (c.Days is { Length: > 0 } && !c.Days.Contains(now.DayOfWeek)) return false;

        if (c.FromTime is not null && c.ToTime is not null) {
            var t = TimeOnly.FromDateTime(now);
            var inside = c.FromTime <= c.ToTime
                ? t >= c.FromTime && t <= c.ToTime
                : t >= c.FromTime || t <= c.ToTime;
            if (!inside) return false;
        }

        return c.ProductIds.Contains(line.ProductId)
               || (line.VariantId is { } v && c.VariantIds.Contains(v))
               || (line.CategoryId is { } cat && c.CategoryIds.Contains(cat));
    }
}
