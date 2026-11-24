using ERP.Application.Common.Security;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.CurrentAccounts;
using ERP.Application.Inventory.Documents;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Inventory.Warehouses;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Transfers;

public sealed record TransferLineDto(Guid Id, Guid ProductId, string ProductCode, string ProductName, Guid? VariantId, string Unit,
                                     decimal Quantity, decimal BaseQuantity);

public sealed record TransferDto(Guid Id, string Number, DateOnly Date, Guid FromWarehouseId, Guid ToWarehouseId, string? Note,
                                 DateTimeOffset CreatedAt, string CreatedBy, IReadOnlyList<TransferLineDto> Lines);

public sealed record TransferRowDto(Guid Id, string Number, DateOnly Date, Guid FromWarehouseId, Guid ToWarehouseId, int LineCount);

/// <summary>Depodan depoya transfer. Hasarlı/karantina depoya geçiş de transferdir (hedef depo o türde olur).</summary>
[Authorize(Permission = InventoryPermissions.Manage)]
public sealed record CreateTransferCommand : IRequest<DocumentCreated> {
    public DateOnly                         Date            { get; init; }
    public Guid                             FromWarehouseId { get; init; }
    public Guid                             ToWarehouseId   { get; init; }
    public string?                          Note            { get; init; }
    public IReadOnlyList<DocumentLineInput> Lines           { get; init; } = [];
}

public sealed class CreateTransferCommandValidator : AbstractValidator<CreateTransferCommand> {
    public CreateTransferCommandValidator() {
        RuleFor(c => c.FromWarehouseId).NotEmpty().WithMessage("Kaynak depo seçilmelidir.");
        RuleFor(c => c.ToWarehouseId).NotEmpty().WithMessage("Hedef depo seçilmelidir.");
        RuleFor(c => c.Date).NotEqual(default(DateOnly)).WithMessage("Tarih zorunludur.");
        RuleFor(c => c.Note).MaximumLength(500).WithMessage("Not en fazla 500 karakter olabilir.");
        this.AddLineRules(c => c.Lines);
    }
}

public sealed class CreateTransferCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    LineResolver          resolver
) : IRequestHandler<CreateTransferCommand, DocumentCreated> {
    public async Task<DocumentCreated> Handle(CreateTransferCommand request, CancellationToken ct) {
        if (request.FromWarehouseId == request.ToWarehouseId)
            throw new AppException(400, "same_warehouse", "Kaynak ve hedef depo aynı olamaz.");

        await WarehouseRules.FindAsync(db, tenant, request.FromWarehouseId, ct);
        await WarehouseRules.FindAsync(db, tenant, request.ToWarehouseId, ct);
        var resolved = await resolver.ResolveAsync(request.Lines.Select(l => l.ToInput()).ToList(), ct);

        await using var transaction = await db.BeginTransactionAsync(ct);
        var transfer = new Transfer {
            Number = await DocumentNumbers.NextAsync(db, "TF", request.Date, ct), Date = request.Date,
            FromWarehouseId = request.FromWarehouseId, ToWarehouseId = request.ToWarehouseId,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = tenant.UserFullName ?? "Sistem"
        };
        foreach (var line in resolved)
            transfer.Lines.Add(new TransferLineEntry {
                ProductId = line.ProductId, VariantId = line.VariantId, Unit = line.Unit, Quantity = line.Quantity,
                BaseQuantity = line.BaseQuantity
            });
        db.Transfers.Add(transfer);
        await db.SaveChangesAsync(ct);

        var lines = resolved.Select(l => new TransferLine(request.FromWarehouseId, request.ToWarehouseId, l.ProductId, l.VariantId,
                                                          l.BaseQuantity, l.Lots, l.Serials, null)).ToList();
        await stock.TransferAsync("transfer", transfer.Id, request.Date, lines, ct);
        await transaction.CommitAsync(ct);
        return new DocumentCreated(transfer.Id, transfer.Number);
    }
}

[Authorize(Permission = InventoryPermissions.Read)]
public sealed record ListTransfersQuery(int Page = 1, int PageSize = 50) : IRequest<PagedResult<TransferRowDto>>;

public sealed class ListTransfersQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<ListTransfersQuery, PagedResult<TransferRowDto>> {
    public async Task<PagedResult<TransferRowDto>> Handle(ListTransfersQuery request, CancellationToken ct) {
        var accessible = (await db.Warehouses.AsNoTracking().ToListAsync(ct)).Where(w => tenant.CanAccessBranch(w.BranchId)).Select(w => w.Id).ToList();
        var query      = db.Transfers.AsNoTracking().Where(t => accessible.Contains(t.FromWarehouseId) || accessible.Contains(t.ToWarehouseId));
        var total      = await query.CountAsync(ct);
        var page       = Math.Max(1, request.Page);
        var pageSize   = Math.Clamp(request.PageSize, 1, 200);
        var rows = await query.OrderByDescending(t => t.Date).ThenByDescending(t => t.Number).Skip((page - 1) * pageSize).Take(pageSize)
                              .Select(t => new TransferRowDto(t.Id, t.Number, t.Date, t.FromWarehouseId, t.ToWarehouseId, t.Lines.Count))
                              .ToListAsync(ct);
        return new PagedResult<TransferRowDto>(rows, total);
    }
}

[Authorize(Permission = InventoryPermissions.Read)]
public sealed record GetTransferQuery(Guid Id) : IRequest<TransferDto>;

public sealed class GetTransferQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<GetTransferQuery, TransferDto> {
    public async Task<TransferDto> Handle(GetTransferQuery request, CancellationToken ct) {
        var transfer = await db.Transfers.AsNoTracking().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == request.Id, ct)
                       ?? throw new NotFoundException(request.Id.ToString(), "Transfer");
        if (!tenant.CanAccessBranch((await db.Warehouses.AsNoTracking().FirstAsync(w => w.Id == transfer.FromWarehouseId, ct)).BranchId)
            && !tenant.CanAccessBranch((await db.Warehouses.AsNoTracking().FirstAsync(w => w.Id == transfer.ToWarehouseId, ct)).BranchId))
            throw new NotFoundException(request.Id.ToString(), "Transfer");

        var ids      = transfer.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var lines = transfer.Lines.Select(l => new TransferLineDto(l.Id, l.ProductId, products[l.ProductId].Code, products[l.ProductId].Name,
                                                                   l.VariantId, l.Unit, l.Quantity, l.BaseQuantity)).ToList();
        return new TransferDto(transfer.Id, transfer.Number, transfer.Date, transfer.FromWarehouseId, transfer.ToWarehouseId, transfer.Note,
                               transfer.CreatedAt, transfer.CreatedBy, lines);
    }
}
