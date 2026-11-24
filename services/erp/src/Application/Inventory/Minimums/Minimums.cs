using ERP.Application.Common.Security;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Inventory.Warehouses;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Minimums;

public sealed record MinimumDto(Guid ProductId, string ProductCode, string ProductName, Guid? VariantId, decimal MinQty, decimal? ReorderQty);

public sealed record BelowMinimumDto(Guid WarehouseId, Guid ProductId, string ProductCode, string ProductName, Guid? VariantId,
                                     decimal Available, decimal MinQty, decimal? ReorderQty);

public sealed record MinimumInput(Guid ProductId, Guid? VariantId, decimal MinQty, decimal? ReorderQty);

[Authorize(Permission = InventoryPermissions.Read)]
public sealed record ListMinimumsQuery(Guid WarehouseId) : IRequest<IReadOnlyList<MinimumDto>>;

public sealed class ListMinimumsQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<ListMinimumsQuery, IReadOnlyList<MinimumDto>> {
    public async Task<IReadOnlyList<MinimumDto>> Handle(ListMinimumsQuery request, CancellationToken ct) {
        await WarehouseRules.FindAsync(db, tenant, request.WarehouseId, ct);
        var rows = await db.MinStockLevels.AsNoTracking().Where(m => m.WarehouseId == request.WarehouseId).ToListAsync(ct);
        var ids  = rows.Select(r => r.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return rows.OrderBy(r => products[r.ProductId].Code)
                   .Select(r => new MinimumDto(r.ProductId, products[r.ProductId].Code, products[r.ProductId].Name, r.VariantId, r.MinQty, r.ReorderQty))
                   .ToList();
    }
}

/// <summary>Deponun minimum stok satırlarının tamamını değiştirir (listede olmayanlar silinir).</summary>
[Authorize(Permission = InventoryPermissions.Manage)]
public sealed record SetMinimumsCommand(Guid WarehouseId, IReadOnlyList<MinimumInput> Rows) : IRequest;

public sealed class SetMinimumsCommandValidator : AbstractValidator<SetMinimumsCommand> {
    public SetMinimumsCommandValidator() {
        RuleFor(c => c.WarehouseId).NotEmpty().WithMessage("Depo seçilmelidir.");
        RuleFor(c => c.Rows).Must(r => r.Select(x => (x.ProductId, x.VariantId)).Distinct().Count() == r.Count)
            .WithMessage("Aynı kalem birden fazla satırda geçiyor.");
        RuleForEach(c => c.Rows).ChildRules(r => {
            r.RuleFor(x => x.ProductId).NotEmpty().WithMessage("Ürün seçilmelidir.");
            r.RuleFor(x => x.MinQty).GreaterThanOrEqualTo(0m).WithMessage("Minimum miktar negatif olamaz.");
            r.RuleFor(x => x.ReorderQty).Must(q => q is null or >= 0m).WithMessage("Sipariş miktarı negatif olamaz.");
        });
    }
}

public sealed class SetMinimumsCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<SetMinimumsCommand> {
    public async Task Handle(SetMinimumsCommand request, CancellationToken ct) {
        await WarehouseRules.FindAsync(db, tenant, request.WarehouseId, ct);
        var ids      = request.Rows.Select(r => r.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Include(p => p.Variants).Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        foreach (var row in request.Rows) {
            var product = products.FirstOrDefault(p => p.Id == row.ProductId) ?? throw new NotFoundException(row.ProductId.ToString(), "Product");
            if (product.Kind == ERP.Domain.Catalog.ProductKind.Service || product.IsBundle)
                throw InventoryErrors.ItemNotStockable($"{product.Code} - {product.Name}");
            if (row.VariantId is { } v && product.Variants.All(x => x.Id != v))
                throw new AppException(400, "invalid_variant", $"{product.Code}: varyant bu ürüne ait değil.");
        }

        var existing = await db.MinStockLevels.Where(m => m.WarehouseId == request.WarehouseId).ToListAsync(ct);
        foreach (var old in existing.Where(o => request.Rows.All(r => r.ProductId != o.ProductId || r.VariantId != o.VariantId)))
            db.MinStockLevels.Remove(old);

        foreach (var row in request.Rows) {
            var level = existing.FirstOrDefault(o => o.ProductId == row.ProductId && o.VariantId == row.VariantId);
            if (level is null) {
                level = new MinStockLevel { WarehouseId = request.WarehouseId, ProductId = row.ProductId, VariantId = row.VariantId };
                db.MinStockLevels.Add(level);
            }

            level.MinQty     = row.MinQty;
            level.ReorderQty = row.ReorderQty;
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Kullanılabilir miktarı minimumun altında kalan kalemler (yalnızca Normal depolar; satın alma ve panel kullanır).</summary>
[Authorize(Permission = InventoryPermissions.Read)]
public sealed record BelowMinimumQuery(Guid? WarehouseId) : IRequest<IReadOnlyList<BelowMinimumDto>>;

public sealed class BelowMinimumQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    BalanceReader         balances
) : IRequestHandler<BelowMinimumQuery, IReadOnlyList<BelowMinimumDto>> {
    public async Task<IReadOnlyList<BelowMinimumDto>> Handle(BelowMinimumQuery request, CancellationToken ct) {
        var warehouses = (await db.Warehouses.AsNoTracking().Where(w => w.Kind == WarehouseKind.Normal).ToListAsync(ct))
            .Where(w => tenant.CanAccessBranch(w.BranchId) && (request.WarehouseId == null || w.Id == request.WarehouseId)).Select(w => w.Id).ToList();
        if (request.WarehouseId is { } id && warehouses.Count == 0) await WarehouseRules.FindAsync(db, tenant, id, ct);

        var levels = await db.MinStockLevels.AsNoTracking().Where(m => warehouses.Contains(m.WarehouseId) && m.MinQty > 0m).ToListAsync(ct);
        if (levels.Count == 0) return [];

        var items    = levels.Select(l => (l.ProductId, l.VariantId)).Distinct().ToList();
        var current  = await balances.ForItemsAsync(items, request.WarehouseId, ct);
        var ids      = levels.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var result = new List<BelowMinimumDto>();
        foreach (var level in levels) {
            var available = current.FirstOrDefault(b => b.WarehouseId == level.WarehouseId && b.ProductId == level.ProductId
                                                        && b.VariantId == level.VariantId)?.Available ?? 0m;
            if (available < level.MinQty)
                result.Add(new BelowMinimumDto(level.WarehouseId, level.ProductId, products[level.ProductId].Code, products[level.ProductId].Name,
                                               level.VariantId, available, level.MinQty, level.ReorderQty));
        }

        return result.OrderBy(r => r.ProductCode).ToList();
    }
}
