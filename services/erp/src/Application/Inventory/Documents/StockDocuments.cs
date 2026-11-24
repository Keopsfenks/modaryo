using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Inventory.Warehouses;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Documents;

public sealed record DocumentLineInput {
    public Guid                      ProductId { get; init; }
    public Guid?                     VariantId { get; init; }
    public string                    Unit      { get; init; } = string.Empty;
    public decimal                   Quantity  { get; init; }
    public decimal?                  UnitCost  { get; init; }
    public IReadOnlyList<LotInput>?  Lots      { get; init; }
    public IReadOnlyList<string>?    Serials   { get; init; }

    public LineInput ToInput() {
        return new LineInput(ProductId, VariantId, Unit, Quantity, UnitCost, Lots, Serials);
    }
}

public static class DocumentRules {
    public static void AddLineRules<T>(this AbstractValidator<T> v, System.Linq.Expressions.Expression<Func<T, IEnumerable<DocumentLineInput>>> lines) {
        v.RuleFor(lines).NotEmpty().WithMessage("En az bir satır girilmelidir.")
            .Must(l => l.Count() <= 500).WithMessage("Bir belgede en fazla 500 satır olabilir.");
        v.RuleForEach(lines).ChildRules(l => {
            l.RuleFor(x => x.ProductId).NotEmpty().WithMessage("Ürün seçilmelidir.");
            l.RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Miktar sıfırdan büyük olmalıdır.");
            l.RuleFor(x => x.Unit).NotEmpty().WithMessage("Birim zorunludur.");
        });
    }
}

public sealed record StockDocumentLineDto(Guid Id, Guid ProductId, string ProductCode, string ProductName, Guid? VariantId, string Unit,
                                          decimal Quantity, decimal BaseQuantity, decimal? UnitCost, string? TrackingJson);

public sealed record StockDocumentDto(Guid Id, string Number, StockDocumentKind Kind, StockDocumentStatus Status, DateOnly Date,
                                      Guid WarehouseId, string? Reason, string? CancelReason, DateTimeOffset CreatedAt,
                                      string CreatedBy, IReadOnlyList<StockDocumentLineDto> Lines);

public sealed record StockDocumentRowDto(Guid Id, string Number, StockDocumentKind Kind, StockDocumentStatus Status, DateOnly Date,
                                         Guid WarehouseId, string? Reason, int LineCount);

public sealed record DocumentCreated(Guid Id, string Number);

/// <summary>Kind: receipt | issue | opening. Birim maliyet yalnızca receipt/opening için kullanılır; verilmezse ortalama maliyet.</summary>
[Authorize(Permission = InventoryPermissions.Manage)]
public sealed record CreateStockDocumentCommand : IRequest<DocumentCreated> {
    public StockDocumentKind                Kind        { get; init; }
    public DateOnly                         Date        { get; init; }
    public Guid                             WarehouseId { get; init; }
    public string?                          Reason      { get; init; }
    public IReadOnlyList<DocumentLineInput> Lines       { get; init; } = [];
}

public sealed class CreateStockDocumentCommandValidator : AbstractValidator<CreateStockDocumentCommand> {
    public CreateStockDocumentCommandValidator() {
        RuleFor(c => c.Kind).IsInEnum().WithMessage("Belge türü geçersiz.");
        RuleFor(c => c.WarehouseId).NotEmpty().WithMessage("Depo seçilmelidir.");
        RuleFor(c => c.Date).NotEqual(default(DateOnly)).WithMessage("Tarih zorunludur.");
        RuleFor(c => c.Reason).MaximumLength(500).WithMessage("Açıklama en fazla 500 karakter olabilir.");
        this.AddLineRules(c => c.Lines);
    }
}

public sealed class CreateStockDocumentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    LineResolver          resolver
) : IRequestHandler<CreateStockDocumentCommand, DocumentCreated> {
    public async Task<DocumentCreated> Handle(CreateStockDocumentCommand request, CancellationToken ct) {
        var warehouse = await WarehouseRules.FindAsync(db, tenant, request.WarehouseId, ct);
        var resolved  = await resolver.ResolveAsync(request.Lines.Select(l => l.ToInput()).ToList(), ct);

        await using var transaction = await db.BeginTransactionAsync(ct);
        var document = await StockDocumentWriter.WriteAsync(db, stock, tenant, request.Kind, request.Date, warehouse.Id, request.Reason, resolved, ct);
        await transaction.CommitAsync(ct);
        return new DocumentCreated(document.Id, document.Number);
    }
}

/// <summary>Belgeyi ve hareketlerini açık işlemde yazar (onaylamaz); açılış içe aktarması da kullanır.</summary>
public static class StockDocumentWriter {
    public static MovementType TypeOf(StockDocumentKind kind) {
        return kind switch {
                   StockDocumentKind.Receipt => MovementType.Receipt,
                   StockDocumentKind.Issue   => MovementType.Issue,
                   _                         => MovementType.Opening
               };
    }

    public static async Task<StockDocument> BeginAsync(IApplicationDbContext db, ICurrentTenant tenant, StockDocumentKind kind, DateOnly date,
                                                       Guid warehouseId, string? reason, CancellationToken ct) {
        var document = new StockDocument {
            Number = await DocumentNumbers.NextAsync(db, "SF", date, ct), Kind = kind, Date = date, WarehouseId = warehouseId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = tenant.UserFullName ?? "Sistem"
        };
        db.StockDocuments.Add(document);
        return document;
    }

    public static async Task WriteLinesAsync(IApplicationDbContext db, IStockService stock, StockDocument document,
                                             IReadOnlyList<ResolvedLine> lines, CancellationToken ct) {
        foreach (var line in lines)
            document.Lines.Add(new StockDocumentLine {
                ProductId = line.ProductId, VariantId = line.VariantId, Unit = line.Unit, Quantity = line.Quantity,
                BaseQuantity = line.BaseQuantity, UnitCost = line.BaseUnitCost, TrackingJson = LineResolver.TrackingJson(line)
            });
        await db.SaveChangesAsync(ct);

        var stockLines = lines.Select(l => new StockLine(document.WarehouseId, l.ProductId, l.VariantId, l.BaseQuantity,
                                                         document.Kind == StockDocumentKind.Issue ? null : l.BaseUnitCost, l.Lots,
                                                         l.Serials, null)).ToList();
        var type = TypeOf(document.Kind);
        if (document.Kind == StockDocumentKind.Issue)
            await stock.IssueAsync(type, "stock-document", document.Id, document.Date, stockLines, ct);
        else
            await stock.ReceiveAsync(type, "stock-document", document.Id, document.Date, stockLines, ct);
    }

    public static async Task<StockDocument> WriteAsync(IApplicationDbContext db, IStockService stock, ICurrentTenant tenant,
                                                       StockDocumentKind kind, DateOnly date, Guid warehouseId, string? reason,
                                                       IReadOnlyList<ResolvedLine> lines, CancellationToken ct) {
        var document = await BeginAsync(db, tenant, kind, date, warehouseId, reason, ct);
        await WriteLinesAsync(db, stock, document, lines, ct);
        return document;
    }
}

[Authorize(Permission = InventoryPermissions.Read)]
public sealed record ListStockDocumentsQuery(string? Kind, Guid? WarehouseId, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<StockDocumentRowDto>>;

public sealed class ListStockDocumentsQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<ListStockDocumentsQuery, PagedResult<StockDocumentRowDto>> {
    public async Task<PagedResult<StockDocumentRowDto>> Handle(ListStockDocumentsQuery request, CancellationToken ct) {
        var query = db.StockDocuments.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Kind)) {
            if (!Enum.TryParse<StockDocumentKind>(request.Kind, true, out var kind))
                throw new AppException(400, "invalid_kind", "Belge türü geçersiz.");
            query = query.Where(d => d.Kind == kind);
        }

        if (request.WarehouseId is { } wh) query = query.Where(d => d.WarehouseId == wh);
        if (request.From is { } from) query = query.Where(d => d.Date >= from);
        if (request.To is { } to) query = query.Where(d => d.Date <= to);

        var accessible = (await db.Warehouses.AsNoTracking().ToListAsync(ct)).Where(w => tenant.CanAccessBranch(w.BranchId)).Select(w => w.Id).ToList();
        query = query.Where(d => accessible.Contains(d.WarehouseId));

        var total    = await query.CountAsync(ct);
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var rows = await query.OrderByDescending(d => d.Date).ThenByDescending(d => d.Number).Skip((page - 1) * pageSize).Take(pageSize)
                              .Select(d => new StockDocumentRowDto(d.Id, d.Number, d.Kind, d.Status, d.Date, d.WarehouseId, d.Reason, d.Lines.Count))
                              .ToListAsync(ct);
        return new PagedResult<StockDocumentRowDto>(rows, total);
    }
}

[Authorize(Permission = InventoryPermissions.Read)]
public sealed record GetStockDocumentQuery(Guid Id) : IRequest<StockDocumentDto>;

public sealed class GetStockDocumentQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session
) : IRequestHandler<GetStockDocumentQuery, StockDocumentDto> {
    public async Task<StockDocumentDto> Handle(GetStockDocumentQuery request, CancellationToken ct) {
        var document = await db.StockDocuments.AsNoTracking().Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == request.Id, ct)
                       ?? throw new NotFoundException(request.Id.ToString(), "StockDocument");
        await WarehouseRules.FindAsync(db, tenant, document.WarehouseId, ct);

        var ids      = document.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var showCost = session.HasPermission(InventoryPermissions.Cost);
        var lines = document.Lines.Select(l => new StockDocumentLineDto(l.Id, l.ProductId, products[l.ProductId].Code, products[l.ProductId].Name,
                                                                         l.VariantId, l.Unit, l.Quantity, l.BaseQuantity,
                                                                         showCost ? l.UnitCost : null, l.TrackingJson)).ToList();
        return new StockDocumentDto(document.Id, document.Number, document.Kind, document.Status, document.Date, document.WarehouseId,
                                    document.Reason, document.CancelReason, document.CreatedAt, document.CreatedBy, lines);
    }
}

[Authorize(Permission = InventoryPermissions.Manage)]
public sealed record CancelStockDocumentCommand(Guid Id, string Reason) : IRequest;

public sealed class CancelStockDocumentCommandValidator : AbstractValidator<CancelStockDocumentCommand> {
    public CancelStockDocumentCommandValidator() {
        RuleFor(c => c.Reason).NotEmpty().WithMessage("İptal nedeni zorunludur.").MaximumLength(500).WithMessage("İptal nedeni en fazla 500 karakter olabilir.");
    }
}

public sealed class CancelStockDocumentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    TimeProvider          time
) : IRequestHandler<CancelStockDocumentCommand> {
    public async Task Handle(CancelStockDocumentCommand request, CancellationToken ct) {
        await using var transaction = await db.BeginTransactionAsync(ct);
        var document = await db.StockDocuments.FirstOrDefaultAsync(d => d.Id == request.Id, ct)
                       ?? throw new NotFoundException(request.Id.ToString(), "StockDocument");
        await WarehouseRules.FindAsync(db, tenant, document.WarehouseId, ct);
        if (document.Status == StockDocumentStatus.Cancelled)
            throw new AppException(409, "document_cancelled", "Bu belge zaten iptal edilmiş.");

        var now = time.GetUtcNow();
        document.Status       = StockDocumentStatus.Cancelled;
        document.CancelReason = request.Reason.Trim();
        document.CancelledAt  = now;
        await db.SaveChangesAsync(ct);

        await stock.ReverseAsync("stock-document", document.Id, DateOnly.FromDateTime(now.UtcDateTime), ct);
        await transaction.CommitAsync(ct);
    }
}
