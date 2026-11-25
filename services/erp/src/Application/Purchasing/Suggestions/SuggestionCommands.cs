using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Documents;
using ERP.Application.Purchasing.Orders;
using ERP.Domain.Inventory;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Suggestions;

public sealed record SuggestionOrderLine(Guid ProductId, Guid? VariantId, Guid WarehouseId, decimal Quantity);

public sealed record SuggestionOrderGroup(Guid SupplierId, IReadOnlyList<SuggestionOrderLine> Lines);

public sealed record CreateSuggestionOrdersInput(IReadOnlyList<SuggestionOrderGroup> Groups);

/// <summary>Seçilen öneri gruplarından tedarikçi (ve depo şubesi) başına bir taslak sipariş üretir. Fiyatlar alış fiyat önceliğinden gelir.</summary>
[Authorize(Permission = PurchasingPermissions.Manage)]
[Authorize(Permission = PurchasingPermissions.PurchasePrice)]
public sealed record CreateSuggestionOrdersCommand(CreateSuggestionOrdersInput Input) : IRequest<IReadOnlyList<PoDto>>;

public sealed class CreateSuggestionOrdersCommandValidator : AbstractValidator<CreateSuggestionOrdersCommand> {
    public CreateSuggestionOrdersCommandValidator() {
        RuleFor(c => c.Input.Groups).NotEmpty().WithMessage("En az bir grup seçilmelidir.");
        RuleForEach(c => c.Input.Groups).ChildRules(g => {
            g.RuleFor(x => x.SupplierId).NotEmpty().WithMessage("Tedarikçi seçilmelidir.");
            g.RuleFor(x => x.Lines).NotEmpty().WithMessage("Grupta en az bir satır olmalıdır.");
            g.RuleForEach(x => x.Lines).ChildRules(l => {
                l.RuleFor(x => x.ProductId).NotEmpty().WithMessage("Kalem seçilmelidir.");
                l.RuleFor(x => x.WarehouseId).NotEmpty().WithMessage("Depo seçilmelidir.");
                l.RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Miktar sıfırdan büyük olmalıdır.")
                 .Must(q => Math.Round(q, 6) == q).WithMessage("Miktar en fazla 6 ondalık basamak içerebilir.");
            });
        });
    }
}

public sealed class CreateSuggestionOrdersCommandHandler(
    IApplicationDbContext db,
    PoBuilder             builder,
    PoMapper              mapper,
    TimeProvider          clock
) : IRequestHandler<CreateSuggestionOrdersCommand, IReadOnlyList<PoDto>> {
    public async Task<IReadOnlyList<PoDto>> Handle(CreateSuggestionOrdersCommand request, CancellationToken ct) {
        var groups = request.Input.Groups;
        if (groups.Select(g => g.SupplierId).Distinct().Count() != groups.Count)
            throw PurchasingErrors.Bad("duplicate_supplier", "Aynı tedarikçi birden fazla grupta geçiyor.");

        var warehouseIds = groups.SelectMany(g => g.Lines).Select(l => l.WarehouseId).Distinct().ToList();
        var warehouses   = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, ct);
        foreach (var id in warehouseIds)
            if (!warehouses.TryGetValue(id, out var w) || w.Kind != WarehouseKind.Normal) throw new NotFoundException(id.ToString(), "Warehouse");

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
        var built = new List<PurchaseOrder>();
        foreach (var group in groups) {
            foreach (var byBranch in group.Lines.GroupBy(l => warehouses[l.WarehouseId].BranchId)) {
                // Aynı kalem birden fazla depodan gelirse tek satırda toplanır.
                var lines = byBranch.GroupBy(l => (l.ProductId, l.VariantId))
                                    .Select(g => new PoLineInput(g.Key.ProductId, g.Key.VariantId, null, g.Sum(x => x.Quantity), null, null, null, null, null, null, null))
                                    .ToList();
                var po = new PurchaseOrder { Status = PurchaseOrderStatus.Draft };
                await builder.ApplyAsync(po, new PoInput(group.SupplierId, byBranch.Key, today, null, "TRY", null, "Minimum stok önerisinden oluşturuldu.", null, lines), ct);
                built.Add(po);
            }
        }

        await using var tx = await db.BeginTransactionAsync(ct);
        foreach (var po in built) {
            po.Number = await DocumentNumbers.NextAsync(db, "SA", po.Date, ct);
            db.PurchaseOrders.Add(po);
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        var result = new List<PoDto>();
        foreach (var po in built) result.Add(await mapper.ToDtoAsync(po, ct));
        return result;
    }
}
