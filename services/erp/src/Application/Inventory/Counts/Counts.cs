using ERP.Application.Common.Security;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory.Documents;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Inventory.Tracking;
using ERP.Application.Inventory.Warehouses;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Counts;

public sealed record CountLineDto(Guid ProductId, string ProductCode, string ProductName, Guid? VariantId, decimal ExpectedQty, decimal? CountedQty);

public sealed record CountDto(Guid Id, string Number, string? Name, Guid WarehouseId, StockCountStatus Status, DateOnly Date,
                              DateTimeOffset SnapshotAt, DateTimeOffset? PostedAt, IReadOnlyList<CountLineDto> Lines);

public sealed record CountRowDto(Guid Id, string Number, string? Name, Guid WarehouseId, StockCountStatus Status, DateOnly Date, int LineCount);

public static class CountErrors {
    public static AppException Closed() {
        return new AppException(409, "count_closed", "Bu sayım kapanmış; değiştirilemez.");
    }

    public static AppException OpenExists() {
        return new AppException(409, "count_open_exists", "Bu depoda zaten açık bir sayım var.");
    }
}

internal static class CountSnapshot {
    /// <summary>Depodaki kalemlerin fiziksel bakiyesi (verilen ana kadar yazılmış hareketler).</summary>
    public static async Task<Dictionary<(Guid Product, Guid? Variant), decimal>> BalancesAsync(IApplicationDbContext db, Guid warehouseId,
                                                                                               DateTimeOffset upTo, CancellationToken ct) {
        var rows = await db.StockMovements.AsNoTracking().Where(m => m.WarehouseId == warehouseId && m.CreatedAt <= upTo)
                           .GroupBy(m => new { m.ProductId, m.VariantId })
                           .Select(g => new { g.Key.ProductId, g.Key.VariantId, Qty = g.Sum(m => m.Quantity) }).ToListAsync(ct);
        return rows.ToDictionary(r => (r.ProductId, r.VariantId), r => r.Qty);
    }
}

/// <summary>Sayım açar: sayım anındaki beklenen miktarlar satırlara yazılır. categoryId verilirse yalnızca o kategori ve altındakiler.</summary>
[Authorize(Permission = InventoryPermissions.Count)]
public sealed record CreateCountCommand(Guid WarehouseId, Guid? CategoryId, string? Name) : IRequest<DocumentCreated>;

public sealed class CreateCountCommandValidator : AbstractValidator<CreateCountCommand> {
    public CreateCountCommandValidator() {
        RuleFor(c => c.WarehouseId).NotEmpty().WithMessage("Depo seçilmelidir.");
        RuleFor(c => c.Name).MaximumLength(500).WithMessage("Sayım adı en fazla 500 karakter olabilir.");
    }
}

public sealed class CreateCountCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    TimeProvider          time
) : IRequestHandler<CreateCountCommand, DocumentCreated> {
    public async Task<DocumentCreated> Handle(CreateCountCommand request, CancellationToken ct) {
        var warehouse = await WarehouseRules.FindAsync(db, tenant, request.WarehouseId, ct);

        // Depo yazma kilidini ÖZEL al: bu depoya yazan açık işlemler biter, yenileri işlem sonuna kadar bekler. Böylece anlık
        // görüntü, yazılmış ama henüz onaylanmamış (CreatedAt < SnapshotAt) hareketleri kaçıramaz; SnapshotAt'ten önce
        // damgalanmış her hareket görüntüye girer, sonrakiler "sayımdan sonra" sayılır.
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockWarehouseExclusiveAsync(warehouse.Id, ct);
        if (await db.StockCounts.AnyAsync(c => c.WarehouseId == warehouse.Id && c.Status == StockCountStatus.Open, ct))
            throw CountErrors.OpenExists();

        var now      = time.GetUtcNow();
        var balances = await CountSnapshot.BalancesAsync(db, warehouse.Id, now, ct);

        HashSet<Guid>? categories = null;
        if (request.CategoryId is { } root) {
            var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
            if (all.All(c => c.Id != root)) throw new NotFoundException(root.ToString(), "Category");

            categories = [root];
            for (var added = true; added;) {
                added = false;
                foreach (var c in all)
                    if (c.ParentId is { } parent && categories.Contains(parent) && categories.Add(c.Id))
                        added = true;
            }
        }

        var ids      = balances.Keys.Select(k => k.Product).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var count = new StockCount {
            WarehouseId = warehouse.Id, Number = await DocumentNumbers.NextAsync(db, "SY", DateOnly.FromDateTime(now.UtcDateTime), ct),
            Status = StockCountStatus.Open, SnapshotAt = now, Date = DateOnly.FromDateTime(now.UtcDateTime),
            Note = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(), CreatedAt = now
        };
        foreach (var ((product, variant), qty) in balances.OrderBy(b => products[b.Key.Product].Code)) {
            var card = products[product];
            if (qty == 0m || TrackingValidator.IsTracked(card) || card.IsBundle) continue;
            if (categories is not null && (card.CategoryId is null || !categories.Contains(card.CategoryId.Value))) continue;

            count.Lines.Add(new StockCountLine { ProductId = product, VariantId = variant, ExpectedQty = qty });
        }

        db.StockCounts.Add(count);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (InventoryErrors.IsUniqueViolation(ex)) {
            throw CountErrors.OpenExists();
        }

        await transaction.CommitAsync(ct);
        return new DocumentCreated(count.Id, count.Number);
    }
}

public sealed record CountLineInput(Guid ProductId, Guid? VariantId, decimal? CountedQty);

/// <summary>Sayılan miktarları yazar. Sayıma girmeyen bir kalem eklenirse beklenen miktarı sayım anına göre hesaplanır.</summary>
[Authorize(Permission = InventoryPermissions.Count)]
public sealed record SetCountLinesCommand(Guid Id, IReadOnlyList<CountLineInput> Lines) : IRequest;

public sealed class SetCountLinesCommandValidator : AbstractValidator<SetCountLinesCommand> {
    public SetCountLinesCommandValidator() {
        RuleFor(c => c.Lines).NotEmpty().WithMessage("En az bir satır girilmelidir.");
        RuleForEach(c => c.Lines).ChildRules(l => {
            l.RuleFor(x => x.ProductId).NotEmpty().WithMessage("Ürün seçilmelidir.");
            l.RuleFor(x => x.CountedQty).Must(q => q is null or >= 0m).WithMessage("Sayılan miktar negatif olamaz.");
        });
    }
}

public sealed class SetCountLinesCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<SetCountLinesCommand> {
    public async Task Handle(SetCountLinesCommand request, CancellationToken ct) {
        var count = await db.StockCounts.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                    ?? throw new NotFoundException(request.Id.ToString(), "StockCount");
        await WarehouseRules.FindAsync(db, tenant, count.WarehouseId, ct);
        if (count.Status != StockCountStatus.Open) throw CountErrors.Closed();

        Dictionary<(Guid Product, Guid? Variant), decimal>? snapshot = null;
        foreach (var input in request.Lines) {
            var line = count.Lines.FirstOrDefault(l => l.ProductId == input.ProductId && l.VariantId == input.VariantId);
            if (line is null) {
                var product = await db.Products.AsNoTracking().Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == input.ProductId, ct)
                              ?? throw new NotFoundException(input.ProductId.ToString(), "Product");
                if (product.Kind == ERP.Domain.Catalog.ProductKind.Service || product.IsBundle)
                    throw InventoryErrors.ItemNotStockable($"{product.Code} - {product.Name}");
                if (TrackingValidator.IsTracked(product))
                    throw new AppException(400, "count_tracked_item", $"{product.Code}: seri/lot takipli ürünler sayım fişiyle sayılamaz.");
                if (input.VariantId is { } v && product.Variants.All(x => x.Id != v))
                    throw new AppException(400, "invalid_variant", $"{product.Code}: varyant bu ürüne ait değil.");

                snapshot ??= await CountSnapshot.BalancesAsync(db, count.WarehouseId, count.SnapshotAt, ct);
                line = new StockCountLine {
                    StockCountId = count.Id, ProductId = input.ProductId, VariantId = input.VariantId,
                    ExpectedQty = snapshot.GetValueOrDefault((input.ProductId, input.VariantId))
                };
                count.Lines.Add(line);
                db.StockCountLines.Add(line);
            }

            line.CountedQty = input.CountedQty is { } q ? decimal.Round(q, 6) : null;
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
///     Sayımı kapatır. Fark = sayılan - sayım anındaki beklenen (şimdiki bakiye değil): sayım sürerken yapılan
///     satış/girişler ikinci kez sayılmaz. Sayılmamış (boş) satırlar atlanır.
/// </summary>
[Authorize(Permission = InventoryPermissions.Count)]
public sealed record PostCountCommand(Guid Id) : IRequest;

public sealed class PostCountCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    TimeProvider          time
) : IRequestHandler<PostCountCommand> {
    public async Task Handle(PostCountCommand request, CancellationToken ct) {
        await using var transaction = await db.BeginTransactionAsync(ct);
        var count = await db.StockCounts.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                    ?? throw new NotFoundException(request.Id.ToString(), "StockCount");
        await WarehouseRules.FindAsync(db, tenant, count.WarehouseId, ct);
        if (count.Status != StockCountStatus.Open) throw CountErrors.Closed();

        // Koşullu tek UPDATE ile sahiplen: eşzamanlı ikinci kapatma 0 satır görür.
        var claimed = await db.StockCounts.Where(c => c.Id == count.Id && c.Status == StockCountStatus.Open)
                              .ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, StockCountStatus.Posted), ct);
        if (claimed == 0) throw CountErrors.Closed();

        var now   = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var gains = new List<StockLine>();
        var loses = new List<StockLine>();
        foreach (var line in count.Lines.Where(l => l.CountedQty is not null)) {
            var diff = line.CountedQty!.Value - line.ExpectedQty;
            if (diff > 0m) gains.Add(new StockLine(count.WarehouseId, line.ProductId, line.VariantId, diff, null, null, null, null));
            else if (diff < 0m) loses.Add(new StockLine(count.WarehouseId, line.ProductId, line.VariantId, -diff, null, null, null, null));
        }

        // Tüm anahtarlar tek sıralı çağrıda baştan kilitlenir: çıkış ve giriş ayrı çağrılarda kilit alırsa eşzamanlı belgelerle
        // ters sırada kilit bekleyip kilitlenebilir. Sonraki servis çağrılarındaki kilitler aynı oturumda anında döner.
        await db.LockStockAsync(gains.Concat(loses).Select(l => (l.WarehouseId, l.ProductId, l.VariantId)), ct);

        // Önce çıkışlar: stok yetersizliği (ör. sayım sırasında çok satış) hiçbir şey yazılmadan reddedilir.
        if (loses.Count > 0) await stock.IssueAsync(MovementType.CountAdjustment, "stock-count", count.Id, today, loses, ct);
        if (gains.Count > 0) await stock.ReceiveAsync(MovementType.CountAdjustment, "stock-count", count.Id, today, gains, ct);

        count.Status   = StockCountStatus.Posted;
        count.PostedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

[Authorize(Permission = InventoryPermissions.Count)]
public sealed record CancelCountCommand(Guid Id) : IRequest;

public sealed class CancelCountCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<CancelCountCommand> {
    public async Task Handle(CancelCountCommand request, CancellationToken ct) {
        var count = await db.StockCounts.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                    ?? throw new NotFoundException(request.Id.ToString(), "StockCount");
        await WarehouseRules.FindAsync(db, tenant, count.WarehouseId, ct);
        if (count.Status != StockCountStatus.Open) throw CountErrors.Closed();

        count.Status = StockCountStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }
}

[Authorize(Permission = InventoryPermissions.Read)]
public sealed record ListCountsQuery(Guid? WarehouseId) : IRequest<IReadOnlyList<CountRowDto>>;

public sealed class ListCountsQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<ListCountsQuery, IReadOnlyList<CountRowDto>> {
    public async Task<IReadOnlyList<CountRowDto>> Handle(ListCountsQuery request, CancellationToken ct) {
        var accessible = (await db.Warehouses.AsNoTracking().ToListAsync(ct)).Where(w => tenant.CanAccessBranch(w.BranchId)).Select(w => w.Id).ToList();
        var query      = db.StockCounts.AsNoTracking().Where(c => accessible.Contains(c.WarehouseId));
        if (request.WarehouseId is { } wh) query = query.Where(c => c.WarehouseId == wh);
        return await query.OrderByDescending(c => c.CreatedAt)
                          .Select(c => new CountRowDto(c.Id, c.Number, c.Note, c.WarehouseId, c.Status, c.Date, c.Lines.Count)).ToListAsync(ct);
    }
}

[Authorize(Permission = InventoryPermissions.Read)]
public sealed record GetCountQuery(Guid Id) : IRequest<CountDto>;

public sealed class GetCountQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<GetCountQuery, CountDto> {
    public async Task<CountDto> Handle(GetCountQuery request, CancellationToken ct) {
        var count = await db.StockCounts.AsNoTracking().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                    ?? throw new NotFoundException(request.Id.ToString(), "StockCount");
        await WarehouseRules.FindAsync(db, tenant, count.WarehouseId, ct);

        var ids      = count.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var lines = count.Lines.OrderBy(l => products[l.ProductId].Code)
                         .Select(l => new CountLineDto(l.ProductId, products[l.ProductId].Code, products[l.ProductId].Name, l.VariantId,
                                                       l.ExpectedQty, l.CountedQty)).ToList();
        return new CountDto(count.Id, count.Number, count.Note, count.WarehouseId, count.Status, count.Date, count.SnapshotAt, count.PostedAt, lines);
    }
}
