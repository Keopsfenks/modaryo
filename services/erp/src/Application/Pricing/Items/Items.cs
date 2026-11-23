using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;
using ERP.Domain.Pricing;

namespace ERP.Application.Pricing.Items;

/// <summary>Bir fiyat satırının ekranda gösterilecek hali (açık satır ya da türetilmiş değer).</summary>
internal sealed record ItemView(
    Guid?                    Id,
    Guid                     ProductId,
    Guid?                    VariantId,
    string                   Unit,
    IReadOnlyList<PriceTier> Tiers,
    uint                     Version,
    bool                     Derived
);

internal static class ItemDtos {
    public static async Task<List<PriceItemDto>> ToDtosAsync(IApplicationDbContext db, IReadOnlyList<ItemView> views, CancellationToken ct) {
        var productIds = views.Select(v => v.ProductId).Distinct().ToList();
        var variantIds = views.Where(v => v.VariantId is not null).Select(v => v.VariantId!.Value).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
                           .Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);
        var variants = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id))
                           .Select(v => new { v.Id, v.Code }).ToDictionaryAsync(v => v.Id, v => v.Code, ct);
        return views.Select(v => new PriceItemDto(
                                v.Id, v.ProductId, products.GetValueOrDefault(v.ProductId)?.Code ?? string.Empty,
                                products.GetValueOrDefault(v.ProductId)?.Name ?? string.Empty, v.VariantId,
                                v.VariantId is { } vid ? variants.GetValueOrDefault(vid) : null, v.Unit, v.Tiers, v.Version,
                                v.Derived)).ToList();
    }

    public static IQueryable<Product> Search(IQueryable<Product> query, string? search) {
        if (string.IsNullOrWhiteSpace(search)) return query;

        var term = PricingRules.FoldedSearch(search);
        return query.Where(p => p.Name.ToLower().Replace("ı", "i").Contains(term)
                                || p.Code.ToLower().Replace("ı", "i").Contains(term));
    }
}

[Authorize(Permission = PricingPermissions.Read)]
public sealed record ListPriceItemsQuery(Guid ListId, string? Search, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<PriceItemDto>>;

public sealed class ListPriceItemsQueryValidator : AbstractValidator<ListPriceItemsQuery> {
    public ListPriceItemsQueryValidator() {
        RuleFor(q => q.Page).InclusiveBetween(1, 100000).WithMessage("Sayfa 1 ile 100000 arasında olmalıdır.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200).WithMessage("Sayfa boyutu 1 ile 200 arasında olmalıdır.");
    }
}

public sealed class ListPriceItemsQueryHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    ITenantSettings       settings
) : IRequestHandler<ListPriceItemsQuery, PagedResult<PriceItemDto>> {
    public async Task<PagedResult<PriceItemDto>> Handle(ListPriceItemsQuery request, CancellationToken ct) {
        var list   = await PricingRules.LoadListAsync(db, session, request.ListId, ct, false);
        var skip   = (request.Page - 1) * request.PageSize;
        var search = request.Search;

        if (list.BaseListId is null) {
            var rows = db.PriceListItems.AsNoTracking().Where(i => i.PriceListId == list.Id);
            var joined = from i in rows
                         join p in ItemDtos.Search(db.Products.AsNoTracking(), search) on i.ProductId equals p.Id
                         select new { Item = i, p.Code };
            var total = await joined.CountAsync(ct);
            var page = await joined.OrderBy(x => x.Code).ThenBy(x => x.Item.Unit).ThenBy(x => x.Item.Id)
                           .Skip(skip).Take(request.PageSize).Select(x => x.Item).ToListAsync(ct);
            var views = page.Select(i => new ItemView(i.Id, i.ProductId, i.VariantId, i.Unit, i.Tiers, i.Version, false)).ToList();
            return new PagedResult<PriceItemDto>(await ItemDtos.ToDtosAsync(db, views, ct), total);
        }

        // Türetilmiş liste: zincirdeki tüm açık satırların anahtarları birleştirilir, değerler bellekte hesaplanır.
        var lists    = await db.PriceLists.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);
        var chain    = PriceGraph.Chain(lists, list.Id);
        var products = ItemDtos.Search(db.Products.AsNoTracking(), search);
        var items = await (from i in db.PriceListItems.AsNoTracking().Where(i => chain.Contains(i.PriceListId))
                           join p in products on i.ProductId equals p.Id
                           select i).ToListAsync(ct);
        var rounding = (await settings.GetAsync<PricingSettings>(ct)).Rounding;
        var graph    = new PriceGraph(lists, items, rounding);

        var keys = items.Select(i => (i.ProductId, i.VariantId, i.Unit)).Distinct().ToList();
        var all  = new List<ItemView>();
        foreach (var (productId, variantId, unit) in keys) {
            if (graph.TiersFor(list.Id, productId, variantId, unit) is not { } found) continue;

            all.Add(new ItemView(found.Row?.Id, productId, variantId, unit, found.Tiers, found.Row?.Version ?? 0, found.Derived));
        }

        var viewProductIds = all.Select(a => a.ProductId).Distinct().ToList();
        var codes = await db.Products.AsNoTracking().Where(p => viewProductIds.Contains(p.Id))
                        .Select(p => new { p.Id, p.Code }).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        var ordered = all.OrderBy(a => codes.GetValueOrDefault(a.ProductId), StringComparer.Ordinal).ThenBy(a => a.Unit, StringComparer.Ordinal)
            .ToList();
        var pageViews = ordered.Skip(skip).Take(request.PageSize).ToList();
        return new PagedResult<PriceItemDto>(await ItemDtos.ToDtosAsync(db, pageViews, ct), ordered.Count);
    }
}

public sealed record PriceRowInput(Guid ProductId, Guid? VariantId, string? Unit, List<PriceTier>? Tiers, uint? Version);

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record UpsertPriceItemsCommand(Guid ListId, List<PriceRowInput>? Rows) : IRequest<PriceItemsResult>;

public sealed class UpsertPriceItemsCommandValidator : AbstractValidator<UpsertPriceItemsCommand> {
    public UpsertPriceItemsCommandValidator() {
        RuleFor(c => c.Rows).NotEmpty().WithMessage("En az bir satır gerekir.")
            .Must(r => r is null || r.Count <= 500).WithMessage("Tek istekte en fazla 500 satır gönderilebilir.");
        RuleForEach(c => c.Rows).Must(r => !string.IsNullOrWhiteSpace(r.Unit)).WithMessage("Birim zorunludur.");
    }
}

public sealed class UpsertPriceItemsCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    PriceHistoryWriter    history
) : IRequestHandler<UpsertPriceItemsCommand, PriceItemsResult> {
    public async Task<PriceItemsResult> Handle(UpsertPriceItemsCommand request, CancellationToken ct) {
        var list = await PricingRules.LoadListAsync(db, session, request.ListId, ct, false);
        var rows = request.Rows!;

        if (rows.Select(r => (r.ProductId, r.VariantId, Unit: r.Unit!.Trim())).Distinct().Count() != rows.Count)
            throw new AppException(400, "duplicate_rows", "Aynı kalem ve birim bir istekte yalnızca bir kez gönderilebilir.");

        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var products   = await db.Products.Include(p => p.Units).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var variantIds = rows.Where(r => r.VariantId is not null).Select(r => r.VariantId!.Value).Distinct().ToList();
        var variants   = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var existing   = await db.PriceListItems.Where(i => i.PriceListId == list.Id && productIds.Contains(i.ProductId)).ToListAsync(ct);

        var touched = new List<PriceListItem>();
        foreach (var row in rows) {
            if (!products.TryGetValue(row.ProductId, out var product))
                throw new NotFoundException(row.ProductId.ToString(), "Product");
            if (row.VariantId is { } vid && (!variants.TryGetValue(vid, out var variant) || variant.ProductId != product.Id))
                throw new NotFoundException(vid.ToString(), "ProductVariant");

            var unit  = row.Unit!.Trim();
            var tiers = PricingRules.NormalizeTiers(row.Tiers);
            PricingRules.EnsureUnit(product, unit);

            var current = existing.FirstOrDefault(i => i.ProductId == row.ProductId && i.VariantId == row.VariantId && i.Unit == unit);
            if (row.Version is { } version && (current is null || current.Version != version))
                throw PricingRules.VersionConflict();

            if (current is null) {
                current = new PriceListItem { PriceListId = list.Id, ProductId = row.ProductId, VariantId = row.VariantId, Unit = unit, Tiers = tiers };
                db.PriceListItems.Add(current);
                existing.Add(current);
                history.Record(PriceHistoryWriter.ScopeList, list.Id, row.ProductId, row.VariantId, unit, null, tiers, list.Kind);
            } else if (!PricingRules.SameTiers(current.Tiers, tiers)) {
                history.Record(PriceHistoryWriter.ScopeList, list.Id, row.ProductId, row.VariantId, unit, current.Tiers.ToList(), tiers,
                               list.Kind);
                current.Tiers = tiers;
            }

            touched.Add(current);
        }

        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            throw PricingRules.VersionConflict();
        } catch (DbUpdateException ex) when (PricingRules.IsUniqueViolation(ex)) {
            throw PricingRules.VersionConflict();
        }

        var views = touched.Select(i => new ItemView(i.Id, i.ProductId, i.VariantId, i.Unit, i.Tiers, i.Version, false)).ToList();
        return new PriceItemsResult(await ItemDtos.ToDtosAsync(db, views, ct));
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record DeletePriceItemCommand(Guid ListId, Guid ItemId) : IRequest;

public sealed class DeletePriceItemCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    PriceHistoryWriter    history
) : IRequestHandler<DeletePriceItemCommand> {
    public async Task Handle(DeletePriceItemCommand request, CancellationToken ct) {
        var list = await PricingRules.LoadListAsync(db, session, request.ListId, ct, false);
        var item = await db.PriceListItems.FirstOrDefaultAsync(i => i.Id == request.ItemId && i.PriceListId == list.Id, ct)
                   ?? throw new NotFoundException(request.ItemId.ToString(), "PriceListItem");

        history.Record(PriceHistoryWriter.ScopeList, list.Id, item.ProductId, item.VariantId, item.Unit, item.Tiers.ToList(), null, list.Kind);
        db.PriceListItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }
}

[Authorize(Permission = PricingPermissions.Manage)]
public sealed record BulkAdjustCommand(Guid ListId, decimal Percent, Guid? CategoryId, Guid? BrandId) : IRequest<BulkAdjustResult>;

public sealed class BulkAdjustCommandValidator : AbstractValidator<BulkAdjustCommand> {
    public BulkAdjustCommandValidator() {
        RuleFor(c => c.Percent).InclusiveBetween(-100m, 1000m).WithMessage("Yüzde -100 ile 1000 arasında olmalıdır.");
    }
}

public sealed class BulkAdjustCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    ITenantSettings       settings,
    PriceHistoryWriter    history
) : IRequestHandler<BulkAdjustCommand, BulkAdjustResult> {
    public async Task<BulkAdjustResult> Handle(BulkAdjustCommand request, CancellationToken ct) {
        var list     = await PricingRules.LoadListAsync(db, session, request.ListId, ct, false);
        var rounding = (await settings.GetAsync<PricingSettings>(ct)).Rounding;

        var items = db.PriceListItems.Where(i => i.PriceListId == list.Id);
        if (request.CategoryId is not null || request.BrandId is not null) {
            var products = db.Products.AsNoTracking().AsQueryable();
            if (request.BrandId is { } brandId) products = products.Where(p => p.BrandId == brandId);
            if (request.CategoryId is { } categoryId) {
                var parents = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
                var ids     = new HashSet<Guid> { categoryId };
                var added   = true;
                while (added) {
                    added = false;
                    foreach (var c in parents)
                        if (c.ParentId is { } pid && ids.Contains(pid) && ids.Add(c.Id))
                            added = true;
                }

                products = products.Where(p => p.CategoryId != null && ids.Contains(p.CategoryId.Value));
            }

            var productIds = await products.Select(p => p.Id).ToListAsync(ct);
            items = items.Where(i => productIds.Contains(i.ProductId));
        }

        var factor  = 1m + request.Percent / 100m;
        var updated = 0;
        foreach (var item in await items.ToListAsync(ct)) {
            var next = item.Tiers.Select(t => new PriceTier(t.MinQty, PriceRounding.Apply(Math.Round(t.Price * factor, 4), rounding)))
                .ToList();
            if (PricingRules.SameTiers(item.Tiers, next)) continue;

            history.Record(PriceHistoryWriter.ScopeList, list.Id, item.ProductId, item.VariantId, item.Unit, item.Tiers.ToList(), next,
                           list.Kind);
            item.Tiers = next;
            updated++;
        }

        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            throw PricingRules.VersionConflict();
        }

        return new BulkAdjustResult(updated);
    }
}
