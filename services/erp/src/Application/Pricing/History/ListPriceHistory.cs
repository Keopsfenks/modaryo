using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Domain.Pricing;

namespace ERP.Application.Pricing.History;

[Authorize(Permission = PricingPermissions.Read)]
public sealed record ListPriceHistoryQuery(Guid? ProductId, Guid? VariantId, string? Scope, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<PriceHistoryDto>>;

public sealed class ListPriceHistoryQueryValidator : AbstractValidator<ListPriceHistoryQuery> {
    public ListPriceHistoryQueryValidator() {
        RuleFor(q => q.Scope).Must(s => s is "list" or "customer" or "card").When(q => !string.IsNullOrEmpty(q.Scope))
            .WithMessage("Kapsam list, customer veya card olmalıdır.");
        RuleFor(q => q.Page).InclusiveBetween(1, 100000).WithMessage("Sayfa 1 ile 100000 arasında olmalıdır.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200).WithMessage("Sayfa boyutu 1 ile 200 arasında olmalıdır.");
    }
}

/// <summary>En yeni önce. Alış türündeki kayıtlar yalnızca fiyat.alis izniyle görünür.</summary>
public sealed class ListPriceHistoryQueryHandler(
    IApplicationDbContext db,
    ICurrentSession       session
) : IRequestHandler<ListPriceHistoryQuery, PagedResult<PriceHistoryDto>> {
    public async Task<PagedResult<PriceHistoryDto>> Handle(ListPriceHistoryQuery request, CancellationToken ct) {
        var query = db.PriceHistory.AsNoTracking().AsQueryable();
        if (!session.HasPermission(PricingPermissions.Purchase)) query = query.Where(h => h.Kind == PriceListKind.Sale);
        if (request.ProductId is { } productId) query                  = query.Where(h => h.ProductId == productId);
        if (request.VariantId is { } variantId) query                  = query.Where(h => h.VariantId == variantId);
        if (!string.IsNullOrEmpty(request.Scope)) query                = query.Where(h => h.Scope == request.Scope);

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(h => h.At).ThenByDescending(h => h.Id)
                       .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var codes = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
                        .Select(p => new { p.Id, p.Code }).ToDictionaryAsync(p => p.Id, p => p.Code, ct);

        var items = rows.Select(r => new PriceHistoryDto(
                                    r.Id, r.At, r.ActorLabel, r.Scope, r.ScopeId, r.ProductId, codes.GetValueOrDefault(r.ProductId),
                                    r.VariantId, r.Unit, Parse(r.BeforeJson), Parse(r.AfterJson), r.Kind)).ToList();
        return new PagedResult<PriceHistoryDto>(items, total);
    }

    private static JsonElement? Parse(string? json) {
        return json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
    }
}
