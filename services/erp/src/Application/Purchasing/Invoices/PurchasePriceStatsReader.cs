using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Invoicing;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Invoices;

public sealed record PriceStatDto(Guid ProductId, Guid? VariantId, string Unit, decimal LastUnitPriceTl, decimal MinUnitPriceTl, decimal MaxUnitPriceTl, DateOnly LastPurchaseDate);

/// <summary>
///     Alış fiyat takibi: kesilmiş alış faturalarından kalem ve birim başına son / en düşük / en yüksek TL birim fiyat (12 ay).
///     Okuma modelidir, fatura kesilince ya da iptal edilince her zaman güncel kalır (ayrı bir tablo tutulmaz).
/// </summary>
public sealed class PurchasePriceStatsReader(IApplicationDbContext db, TimeProvider clock) {
    public async Task<IReadOnlyList<PriceStatDto>> GetAsync(Guid? productId, Guid? variantId, CancellationToken ct) {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
        var from  = today.AddMonths(-12);
        var rows = await db.InvoiceLines.AsNoTracking().Where(l => l.ProductId != null && l.Quantity > 0m && (productId == null || l.ProductId == productId)
                                                                   && (variantId == null || l.VariantId == variantId))
                           .Join(db.Invoices.Where(i => i.Kind == InvoiceKind.Purchase && i.Status == InvoiceStatus.Issued && i.Date > from && i.Date <= today),
                                 l => l.InvoiceId, i => i.Id, (l, i) => new { l.ProductId, l.VariantId, l.Unit, i.Date, l.NetTl, l.OtvTl, l.Quantity })
                           .ToListAsync(ct);

        var result = new List<PriceStatDto>();
        foreach (var group in rows.GroupBy(r => (r.ProductId!.Value, r.VariantId, r.Unit))) {
            var purchases = group.Select(r => (r.Date, Math.Round((r.NetTl + r.OtvTl) / r.Quantity, 6, MidpointRounding.AwayFromZero)));
            if (PurchasePriceStat.From(group.Key.Item1, group.Key.VariantId, group.Key.Unit, purchases, today) is { } stat)
                result.Add(new PriceStatDto(stat.ProductId, stat.VariantId, stat.Unit, stat.LastUnitPriceTl, stat.MinUnitPriceTl, stat.MaxUnitPriceTl, stat.LastPurchaseDate));
        }

        return result;
    }
}

[Authorize(Permission = PurchasingPermissions.Read)]
[Authorize(Permission = PurchasingPermissions.PurchasePrice)]
public sealed record GetPriceStatsQuery(Guid? ProductId, Guid? VariantId) : IRequest<IReadOnlyList<PriceStatDto>>;

public sealed class GetPriceStatsQueryHandler(PurchasePriceStatsReader reader) : IRequestHandler<GetPriceStatsQuery, IReadOnlyList<PriceStatDto>> {
    public Task<IReadOnlyList<PriceStatDto>> Handle(GetPriceStatsQuery request, CancellationToken ct) {
        return reader.GetAsync(request.ProductId, request.VariantId, ct);
    }
}
