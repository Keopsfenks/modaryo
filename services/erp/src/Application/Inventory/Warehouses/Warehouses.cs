using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Warehouses;

public sealed record WarehouseDto(
    Guid          Id,
    Guid          BranchId,
    string        Code,
    string        Name,
    WarehouseKind Kind,
    bool          IsDefault,
    bool          IsActive,
    bool          InUse
);

public abstract record WarehouseInput {
    public string        Code { get; init; } = string.Empty;
    public string        Name { get; init; } = string.Empty;
    public WarehouseKind Kind { get; init; } = WarehouseKind.Normal;
}

public static class WarehouseRules {
    public static void AddRules<T>(this AbstractValidator<T> v) where T : WarehouseInput {
        v.RuleFor(w => w.Code).NotEmpty().WithMessage("Depo kodu zorunludur.")
            .Must(c => c is null || c.Trim().Length <= 30).WithMessage("Depo kodu en fazla 30 karakter olabilir.");
        v.RuleFor(w => w.Name).NotEmpty().WithMessage("Depo adı zorunludur.")
            .Must(n => n is null || n.Trim().Length <= 100).WithMessage("Depo adı en fazla 100 karakter olabilir.");
        v.RuleFor(w => w.Kind).IsInEnum().WithMessage("Depo türü geçersiz.");
    }

    public static WarehouseDto ToDto(Warehouse w, bool inUse) {
        return new WarehouseDto(w.Id, w.BranchId, w.Code, w.Name, w.Kind, w.IsDefault, w.IsActive, inUse);
    }

    /// <summary>Depoda hareket, sayım, rezervasyon ya da seri kaydı var mı?</summary>
    public static async Task<bool> InUseAsync(IApplicationDbContext db, Guid warehouseId, CancellationToken ct) {
        return await db.StockMovements.AnyAsync(m => m.WarehouseId == warehouseId, ct)
               || await db.StockCounts.AnyAsync(c => c.WarehouseId == warehouseId, ct)
               || await db.Reservations.AnyAsync(r => r.WarehouseId == warehouseId, ct)
               || await db.SerialNumbers.AnyAsync(s => s.WarehouseId == warehouseId, ct)
               || await db.PosRegisters.AnyAsync(r => r.WarehouseId == warehouseId, ct);
    }

    public static async Task<Warehouse> FindAsync(IApplicationDbContext db, ICurrentTenant tenant, Guid id, CancellationToken ct) {
        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, ct)
                        ?? throw new NotFoundException(id.ToString(), "Warehouse");
        BranchGuard.Ensure(tenant, warehouse.BranchId);
        return warehouse;
    }

    public static async Task EnsureCodeFreeAsync(IApplicationDbContext db, string code, Guid? exceptId, CancellationToken ct) {
        if (await db.Warehouses.AnyAsync(w => w.Code == code && w.Id != exceptId, ct)) throw InventoryErrors.CodeTaken(code);
    }
}

/// <summary>Şubenin varsayılan Normal deposunu verir; yoksa "{şube adı} Depo" adıyla oluşturur.</summary>
public sealed class DefaultWarehouseService(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IBranchDirectory      branches
) {
    /// <summary>
    ///     Oturumun erişebildiği her aktif şubenin varsayılan deposunu (yoksa) oluşturur; tekrar çağrılması zararsızdır.
    ///     Depo listesi ilk açıldığında çalışır: şube kimlik servisinde sonradan açılsa da depo ilk stok erişiminde hazır olur.
    /// </summary>
    public async Task EnsureAllAsync(CancellationToken ct) {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var active   = (await branches.ListAsync(tenantId, ct)).Where(b => b.IsActive && tenant.CanAccessBranch(b.Id)).ToList();
        if (active.Count == 0) return;

        var have = (await db.Warehouses.AsNoTracking().Where(w => w.IsDefault).Select(w => w.BranchId).ToListAsync(ct)).ToHashSet();
        foreach (var branch in active.Where(b => !have.Contains(b.Id)))
            await EnsureAsync(branch.Id, ct);
    }

    public async Task<Warehouse> EnsureAsync(Guid branchId, CancellationToken ct) {
        BranchGuard.Ensure(tenant, branchId);
        var existing = await db.Warehouses.FirstOrDefaultAsync(w => w.BranchId == branchId && w.IsDefault, ct);
        if (existing is not null) return existing;

        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var branch = (await branches.ListAsync(tenantId, ct)).FirstOrDefault(b => b.Id == branchId && b.IsActive)
                     ?? throw InventoryErrors.BranchNotFound();

        var baseCode = $"{branch.Code}-DEPO";
        if (baseCode.Length > 30) baseCode = baseCode[..30];
        var code = baseCode;
        for (var n = 2; await db.Warehouses.AnyAsync(w => w.Code == code, ct); n++) {
            var suffix = $"-{n}";
            code = baseCode[..Math.Min(baseCode.Length, 30 - suffix.Length)] + suffix;
        }

        var name      = $"{branch.Name} Depo";
        var warehouse = new Warehouse {
            BranchId = branchId, Code = code, Name = name.Length > 100 ? name[..100] : name,
            Kind = WarehouseKind.Normal, IsDefault = true
        };
        db.Warehouses.Add(warehouse);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (InventoryErrors.IsUniqueViolation(ex)) {
            // Aynı anda başka bir istek varsayılan depoyu oluşturdu.
            db.ClearTracking();
            return await db.Warehouses.FirstAsync(w => w.BranchId == branchId && w.IsDefault, ct);
        }

        return warehouse;
    }
}

[Authorize(Permission = InventoryPermissions.Read)]
public sealed record ListWarehousesQuery : IRequest<IReadOnlyList<WarehouseDto>>;

public sealed class ListWarehousesQueryHandler(
    IApplicationDbContext   db,
    ICurrentTenant          tenant,
    DefaultWarehouseService defaults
) : IRequestHandler<ListWarehousesQuery, IReadOnlyList<WarehouseDto>> {
    public async Task<IReadOnlyList<WarehouseDto>> Handle(ListWarehousesQuery request, CancellationToken ct) {
        await defaults.EnsureAllAsync(ct);
        var warehouses = (await db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(ct))
            .Where(w => tenant.CanAccessBranch(w.BranchId)).ToList();
        var used = (await db.StockMovements.AsNoTracking().Select(m => m.WarehouseId).Distinct().ToListAsync(ct))
            .Concat(await db.StockCounts.AsNoTracking().Select(c => c.WarehouseId).Distinct().ToListAsync(ct))
            .Concat(await db.Reservations.AsNoTracking().Select(r => r.WarehouseId).Distinct().ToListAsync(ct))
            .ToHashSet();
        return warehouses.Select(w => WarehouseRules.ToDto(w, used.Contains(w.Id))).ToList();
    }
}

[Authorize(Permission = InventoryPermissions.Read)]
public sealed record GetDefaultWarehouseQuery(Guid BranchId) : IRequest<WarehouseDto>;

public sealed class GetDefaultWarehouseQueryHandler(
    DefaultWarehouseService defaults,
    IApplicationDbContext   db
) : IRequestHandler<GetDefaultWarehouseQuery, WarehouseDto> {
    public async Task<WarehouseDto> Handle(GetDefaultWarehouseQuery request, CancellationToken ct) {
        var warehouse = await defaults.EnsureAsync(request.BranchId, ct);
        return WarehouseRules.ToDto(warehouse, await WarehouseRules.InUseAsync(db, warehouse.Id, ct));
    }
}

[Authorize(Permission = InventoryPermissions.Manage)]
public sealed record CreateWarehouseCommand : WarehouseInput, IRequest<WarehouseDto> {
    public Guid BranchId { get; init; }
}

public sealed class CreateWarehouseCommandValidator : AbstractValidator<CreateWarehouseCommand> {
    public CreateWarehouseCommandValidator() {
        this.AddRules();
        RuleFor(c => c.BranchId).NotEmpty().WithMessage("Şube seçilmelidir.");
    }
}

public sealed class CreateWarehouseCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IBranchDirectory      branches
) : IRequestHandler<CreateWarehouseCommand, WarehouseDto> {
    public async Task<WarehouseDto> Handle(CreateWarehouseCommand request, CancellationToken ct) {
        BranchGuard.Ensure(tenant, request.BranchId);
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        if (!await branches.ExistsActiveAsync(tenantId, request.BranchId, ct)) throw InventoryErrors.BranchNotFound();

        var code = request.Code.Trim();
        await WarehouseRules.EnsureCodeFreeAsync(db, code, null, ct);

        var warehouse = new Warehouse {
            BranchId = request.BranchId, Code = code, Name = request.Name.Trim(), Kind = request.Kind
        };
        db.Warehouses.Add(warehouse);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (InventoryErrors.IsUniqueViolation(ex)) {
            throw InventoryErrors.CodeTaken(code);
        }

        return WarehouseRules.ToDto(warehouse, false);
    }
}

[Authorize(Permission = InventoryPermissions.Manage)]
public sealed record UpdateWarehouseCommand : WarehouseInput, IRequest<WarehouseDto> {
    public Guid Id { get; init; }
}

public sealed class UpdateWarehouseCommandValidator : AbstractValidator<UpdateWarehouseCommand> {
    public UpdateWarehouseCommandValidator() {
        this.AddRules();
    }
}

public sealed class UpdateWarehouseCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<UpdateWarehouseCommand, WarehouseDto> {
    public async Task<WarehouseDto> Handle(UpdateWarehouseCommand request, CancellationToken ct) {
        var warehouse = await WarehouseRules.FindAsync(db, tenant, request.Id, ct);
        if (warehouse.IsDefault && request.Kind != WarehouseKind.Normal) throw InventoryErrors.WarehouseIsDefault();

        var code = request.Code.Trim();
        await WarehouseRules.EnsureCodeFreeAsync(db, code, warehouse.Id, ct);

        warehouse.Code = code;
        warehouse.Name = request.Name.Trim();
        warehouse.Kind = request.Kind;
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (InventoryErrors.IsUniqueViolation(ex)) {
            throw InventoryErrors.CodeTaken(code);
        }

        return WarehouseRules.ToDto(warehouse, await WarehouseRules.InUseAsync(db, warehouse.Id, ct));
    }
}

[Authorize(Permission = InventoryPermissions.Manage)]
public sealed record SetWarehouseActiveCommand(Guid Id, bool Active) : IRequest;

public sealed class SetWarehouseActiveCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<SetWarehouseActiveCommand> {
    public async Task Handle(SetWarehouseActiveCommand request, CancellationToken ct) {
        var warehouse = await WarehouseRules.FindAsync(db, tenant, request.Id, ct);
        if (!request.Active && warehouse.IsDefault) throw InventoryErrors.WarehouseIsDefault();

        warehouse.IsActive = request.Active;
        await db.SaveChangesAsync(ct);
    }
}

[Authorize(Permission = InventoryPermissions.Manage)]
public sealed record DeleteWarehouseCommand(Guid Id) : IRequest;

public sealed class DeleteWarehouseCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<DeleteWarehouseCommand> {
    public async Task Handle(DeleteWarehouseCommand request, CancellationToken ct) {
        var warehouse = await WarehouseRules.FindAsync(db, tenant, request.Id, ct);
        if (warehouse.IsDefault) throw InventoryErrors.WarehouseIsDefault();
        if (await WarehouseRules.InUseAsync(db, warehouse.Id, ct)) throw InventoryErrors.WarehouseInUse();

        db.Warehouses.Remove(warehouse);
        await db.SaveChangesAsync(ct);
    }
}
