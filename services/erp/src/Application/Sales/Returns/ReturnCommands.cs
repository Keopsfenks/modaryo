using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Documents;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Inventory.Tracking;
using ERP.Application.Invoicing;
using ERP.Application.Invoicing.Posting;
using ERP.Application.Sales.Invoicing;
using ERP.Application.Sales.Shipments;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Returns;

public sealed record ReturnLineInput(Guid InvoiceLineId, decimal Quantity, IReadOnlyList<InvoiceLot>? Lots, IReadOnlyList<string>? Serials);

public sealed record ReturnInput(Guid InvoiceId, WarehouseKind WarehouseKind, Guid WarehouseId, DateOnly? Date, string? Reason, IReadOnlyList<ReturnLineInput> Lines);

public sealed record ReturnLineDto(Guid Id, int Sort, Guid InvoiceLineId, Guid? SalesOrderLineId, string Description, string Unit, decimal Quantity, decimal? Width, decimal? Length, decimal? UnitCost);

public sealed record ReturnDto(
    Guid                       Id,
    string                     Number,
    SalesReturnStatus          Status,
    Guid                       InvoiceId,
    string?                    InvoiceNumber,
    Guid?                      SalesOrderId,
    Guid                       CurrentAccountId,
    string                     CustomerName,
    DateOnly                   Date,
    Guid                       WarehouseId,
    WarehouseKind              WarehouseKind,
    string?                    Reason,
    Guid?                      ReturnInvoiceId,
    uint                       Version,
    IReadOnlyList<ReturnLineDto> Lines
);

public sealed record ReturnRowDto(Guid Id, string Number, SalesReturnStatus Status, Guid InvoiceId, string? InvoiceNumber, Guid CurrentAccountId, string CustomerName, DateOnly Date);

public sealed record ReturnListResult(IReadOnlyList<ReturnRowDto> Items, int Total);

public sealed class ReturnMapper(IApplicationDbContext db, ICurrentSession session) {
    public async Task<ReturnDto> ToDtoAsync(SalesReturn r, CancellationToken ct) {
        var showCost = session.HasPermission(SalesPermissions.Cost); // birim maliyet maliyet.read ister
        var invoice  = await db.Invoices.AsNoTracking().Where(i => i.Id == r.InvoiceId).Select(i => i.Number).FirstOrDefaultAsync(ct);
        var customer = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == r.CurrentAccountId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        return new ReturnDto(r.Id, r.Number, r.Status, r.InvoiceId, invoice, r.SalesOrderId, r.CurrentAccountId, customer ?? string.Empty, r.Date, r.WarehouseId, r.WarehouseKind, r.Reason,
                             r.ReturnInvoiceId, r.Version,
                             r.Lines.OrderBy(l => l.Sort).Select(l => new ReturnLineDto(l.Id, l.Sort, l.InvoiceLineId, l.SalesOrderLineId, l.Description, l.Unit, l.Quantity, l.Width, l.Length, showCost ? l.UnitCost : null)).ToList());
    }
}

[Authorize(Permission = SalesPermissions.Return)]
public sealed record CreateReturnCommand(ReturnInput Input) : IRequest<ReturnDto>;

public sealed class CreateReturnCommandValidator : AbstractValidator<CreateReturnCommand> {
    public CreateReturnCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.InvoiceId).NotEmpty().WithMessage("Fatura seçilmelidir.");
        RuleFor(c => c.Input.WarehouseId).NotEmpty().WithMessage("Depo seçilmelidir.");
        RuleFor(c => c.Input.WarehouseKind).IsInEnum().WithMessage("Depo türü geçersiz.");
        RuleFor(c => c.Input.Lines).NotEmpty().WithMessage("İade için en az bir satır seçilmelidir.");
        RuleForEach(c => c.Input.Lines).ChildRules(l => {
            l.RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("İade miktarı sıfırdan büyük olmalıdır.").Must(q => Math.Round(q, 6) == q).WithMessage("İade miktarı en fazla 6 ondalık basamak içerebilir.");
        });
        RuleFor(c => c.Input.Reason).MaximumLength(500).WithMessage("İade nedeni en fazla 500 karakter olabilir.");
    }
}

public sealed class CreateReturnCommandHandler(
    IApplicationDbContext db,
    ReturnCreator         creator,
    ReturnMapper          mapper
) : IRequestHandler<CreateReturnCommand, ReturnDto> {
    public async Task<ReturnDto> Handle(CreateReturnCommand request, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            var ret = await creator.CreateAsync(request.Input, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(ret, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw SalesErrors.VersionConflict();
        }
    }
}

/// <summary>
///     İade belgesi kesimi (stok girişi, P4 iade taslağı, sipariş iade miktarı). Çağıran açık bir işlem sağlar ve onaylar; kilitler (cari, sipariş, stok) bu sınıfta
///     sırayla alınır, çağıran daha önce aldıysa yeniden almak zararsızdır. Sevkiyat "iade edildi" akışı da bunu kendi işlemi içinde çağırır.
/// </summary>
public sealed class ReturnCreator(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    IOutbox               outbox,
    InvoiceBuilder        builder,
    TimeProvider          clock
) {
    public async Task<SalesReturn> CreateAsync(ReturnInput input, CancellationToken ct) {
        var original = await db.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == input.InvoiceId, ct)
                       ?? throw new NotFoundException(input.InvoiceId.ToString(), "Invoice");
        BranchGuard.Ensure(tenant, original.BranchId);
        if (original.Kind != InvoiceKind.Sales) throw SalesErrors.Bad("return_of_mismatch", "İade yalnızca satış faturasına yapılabilir.");

        var lineIds = input.Lines.Select(l => l.InvoiceLineId).ToList();
        if (lineIds.Distinct().Count() != lineIds.Count) throw SalesErrors.Bad("duplicate_line", "Aynı fatura satırı bir iadede yalnızca bir kez yer alabilir.");

        // Sipariş bağı: fatura satırı -> sevk satırı -> sipariş satırı (iade miktarı ve kilit sırası için, kilitlerden önce okunur).
        var shipmentLineIds = original.Lines.Where(l => l.SourceLineId != null).Select(l => l.SourceLineId!.Value).ToList();
        var shipmentLines   = original.SourceType == ShipmentInvoiceLinks.Source && shipmentLineIds.Count > 0
                                  ? await db.ShipmentLines.AsNoTracking().Where(l => shipmentLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct)
                                  : new Dictionary<Guid, ShipmentLine>();
        Guid? orderId = shipmentLines.Count > 0
                            ? await db.Shipments.AsNoTracking().Where(s => s.Id == shipmentLines.Values.First().ShipmentId).Select(s => (Guid?)s.SalesOrderId).FirstOrDefaultAsync(ct)
                            : null;

        // İade, fatura kesimi ve iptaliyle aynı cari kilidini alır: paralel iadeler iade edilebilir miktarı birlikte aşamaz.
        await db.LockCurrentAccountAsync(original.CurrentAccountId, ct);
        if (orderId is { } oid) await db.LockSalesOrderAsync(oid, ct);

        var status = await db.Invoices.AsNoTracking().Where(i => i.Id == original.Id).Select(i => (InvoiceStatus?)i.Status).FirstOrDefaultAsync(ct);
        if (status == InvoiceStatus.Cancelled) throw SalesErrors.Conflict("original_cancelled", "Asıl fatura iptal edilmiş; iade yapılamaz.");
        if (status != InvoiceStatus.Issued) throw SalesErrors.Conflict("return_of_not_issued", "İade yalnızca kesilmiş faturaya yapılabilir.");

        var warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == input.WarehouseId, ct) ?? throw new NotFoundException(input.WarehouseId.ToString(), "Warehouse");
        BranchGuard.Ensure(tenant, warehouse.BranchId);
        if (warehouse.Kind != input.WarehouseKind)
            throw SalesErrors.Bad("warehouse_kind_mismatch", $"Seçilen depo {KindName(input.WarehouseKind)} türünde değil.");
        if (!warehouse.IsActive) throw SalesErrors.Conflict("warehouse_inactive", "Depo pasif.");
        if (warehouse.BranchId != original.BranchId) throw SalesErrors.Bad("warehouse_branch_mismatch", "Depo, faturanın şubesine ait değil.");

        var picked = new List<(InvoiceLine Line, ReturnLineInput Input)>();
        foreach (var l in input.Lines) {
            var line = original.Lines.FirstOrDefault(x => x.Id == l.InvoiceLineId) ?? throw SalesErrors.Bad("return_line_invalid", "İade satırı faturaya ait değil.");
            picked.Add((line, l));
        }

        // İade edilmiş miktar: iptal edilmemiş iade belgeleri + belge dışı (elle) kesilmiş iade faturaları.
        var docReturned = (await db.SalesReturnLines.AsNoTracking().Where(l => lineIds.Contains(l.InvoiceLineId))
                                   .Join(db.SalesReturns.Where(r => r.Status != SalesReturnStatus.Cancelled), l => l.SalesReturnId, r => r.Id, (l, _) => new { l.InvoiceLineId, l.Quantity })
                                   .ToListAsync(ct)).GroupBy(x => x.InvoiceLineId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var manualReturned = (await db.InvoiceLines.AsNoTracking().Where(l => l.SourceLineId != null && lineIds.Contains(l.SourceLineId.Value))
                                      .Join(db.Invoices.Where(i => i.Kind == InvoiceKind.SalesReturn && i.Status == InvoiceStatus.Issued && i.SourceType != ReturnService.Source),
                                            l => l.InvoiceId, i => i.Id, (l, _) => new { Source = l.SourceLineId!.Value, l.Quantity }).ToListAsync(ct))
                             .GroupBy(x => x.Source).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        foreach (var (line, l) in picked) {
            var remaining = line.Quantity - docReturned.GetValueOrDefault(line.Id) - manualReturned.GetValueOrDefault(line.Id);
            if (l.Quantity > remaining)
                throw SalesErrors.Bad("return_exceeds_invoiced", $"\"{line.Description}\" için iade edilebilecek en fazla miktar {Math.Max(0m, remaining):0.######}.");
        }

        // Stok: ürünlü, hizmet olmayan satırlar asıl çıkış maliyetiyle depoya girer.
        var productIds = picked.Where(p => p.Line.ProductId != null).Select(p => p.Line.ProductId!.Value).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        // Stok girişi yalnızca asıl çıkışı olan faturalarda: sevkten kesilen fatura (çıkış sevkte) ya da stoku etkileyen fatura. Aksi halde yalnızca cari alacaklanır.
        var issuedStock = original.SourceType == ShipmentInvoiceLinks.Source || original.AffectsStock;
        var stockLines = new List<StockLine>();
        var costs      = new Dictionary<Guid, decimal?>();
        var keys       = new List<(Guid, Guid, Guid?)>();
        foreach (var (line, l) in picked) {
            if (!issuedStock) continue;
            if (line.ProductId is not { } pid || products[pid] is not { Kind: not ProductKind.Service } product) continue;
            if (product.IsBundle) throw SalesErrors.Bad("bundle_return_unsupported", $"\"{line.Description}\" set ürünüdür; bileşenleri ayrı iade edilmelidir.");

            var baseQuantity = Math.Round(l.Quantity * line.Factor, 6, MidpointRounding.AwayFromZero);
            var cost         = await OriginalCostAsync(original, line, shipmentLines, ct);
            costs[line.Id] = cost;
            var lots = l.Lots?.Select(x => new LotInput(x.LotNo, x.Expiry, x.Quantity, x.LotId)).ToList();
            stockLines.Add(new StockLine(warehouse.Id, pid, line.VariantId, baseQuantity, cost, lots is { Count: > 0 } ? lots : null, InvoiceRules.NormalizeSerials(l.Serials), line.Id));
            keys.Add((warehouse.Id, pid, line.VariantId));
            if (TrackingValidator.IsTracked(product)) keys.Add(TrackingValidator.ItemKey(pid));
        }

        var date = input.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
        var ret  = new SalesReturn {
            Number = await DocumentNumbers.NextAsync(db, "IA", date, ct), Status = SalesReturnStatus.Posted, InvoiceId = original.Id, SalesOrderId = orderId,
            CurrentAccountId = original.CurrentAccountId, BranchId = original.BranchId, Date = date, WarehouseId = warehouse.Id, WarehouseKind = warehouse.Kind,
            Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim()
        };
        var sort = 1;
        foreach (var (line, l) in picked) {
            var (width, length) = Dimensions(line, l.Quantity);
            ret.Lines.Add(new SalesReturnLine {
                SalesReturnId = ret.Id, Sort = sort++, InvoiceLineId = line.Id, SalesOrderLineId = line.SourceLineId is { } sid && shipmentLines.TryGetValue(sid, out var sl) ? sl.SalesOrderLineId : null,
                ProductId = line.ProductId, VariantId = line.VariantId, Description = line.Description, Unit = line.Unit, Factor = line.Factor, Quantity = l.Quantity,
                Width = width, Length = length, UnitCost = costs.GetValueOrDefault(line.Id)
            });
        }

        // Stok kilitleri tek çağrıda, girişten önce.
        if (keys.Count > 0) await db.LockStockAsync(keys, ct);
        db.SalesReturns.Add(ret);
        await db.SaveChangesAsync(ct);
        if (stockLines.Count > 0) await stock.ReceiveAsync(MovementType.SalesReturn, ReturnService.StockSource, ret.Id, date, stockLines, ct);

        // P4 SalesReturn taslağı: fiyatlar asıl faturadan; stok etkisi yok (stok belgede girdi).
        var body  = ReturnBody(original, picked, date);
        var built = await builder.BuildAsync(body, null, false, ct);
        var draft = new Invoice { Status = InvoiceStatus.Draft, SourceType = ReturnService.Source, SourceId = ret.Id };
        InvoiceBuilder.Apply(draft, body, built);
        foreach (var line in built.Lines) {
            line.InvoiceId = draft.Id;
            draft.Lines.Add(line);
        }

        foreach (var due in built.Due) {
            due.InvoiceId = draft.Id;
            draft.DueSchedule.Add(due);
        }

        db.Invoices.Add(draft);
        ret.ReturnInvoiceId = draft.Id;

        if (orderId is { } orderKey) {
            var orderLines = await db.SalesOrderLines.Where(l => l.SalesOrderId == orderKey).ToListAsync(ct);
            foreach (var rl in ret.Lines.Where(l => l.SalesOrderLineId != null)) {
                var target = orderLines.First(l => l.Id == rl.SalesOrderLineId);
                target.ReturnedQuantity += Math.Round(rl.Quantity * rl.Factor / target.Factor, 6, MidpointRounding.AwayFromZero);
            }
        }

        outbox.Enqueue("sales.return-created", new { returnId = ret.Id, number = ret.Number, invoiceId = original.Id, kind = ret.WarehouseKind.ToString() });
        await db.SaveChangesAsync(ct);
        return ret;
    }

    private static string KindName(WarehouseKind kind) {
        return kind switch { WarehouseKind.Quarantine => "karantina", WarehouseKind.Damaged => "hasarlı", _ => "normal" };
    }

    /// <summary>Asıl çıkışın temel birim maliyeti: sevk çıkışı (sevkten kesilen faturada) ya da faturanın kendi stok çıkışı; bulunamazsa null (güncel ortalama).</summary>
    private async Task<decimal?> OriginalCostAsync(Invoice original, InvoiceLine line, IReadOnlyDictionary<Guid, ShipmentLine> shipmentLines, CancellationToken ct) {
        var query = db.StockMovements.AsNoTracking().Where(m => m.Quantity < 0m && m.ProductId == line.ProductId && m.VariantId == line.VariantId);
        if (original.SourceType == ShipmentInvoiceLinks.Source && line.SourceLineId is { } sid && shipmentLines.TryGetValue(sid, out var sl))
            query = query.Where(m => m.SourceType == ShipmentStock.SourceType && m.SourceId == sl.ShipmentId && m.SourceLineId == sl.SalesOrderLineId);
        else
            query = query.Where(m => m.SourceType == InvoicePoster.StockSource && m.SourceId == original.Id && m.SourceLineId == line.Id);

        var rows = await query.Select(m => new { m.Quantity, m.TotalCost }).ToListAsync(ct);
        var qty  = rows.Sum(r => Math.Abs(r.Quantity));
        return qty <= 0m ? null : Math.Round(rows.Sum(r => Math.Abs(r.TotalCost)) / qty, 6, MidpointRounding.AwayFromZero);
    }

    private static (decimal? Width, decimal? Length) Dimensions(InvoiceLine line, decimal quantity) {
        if (line.Length is not { } length) return (null, null);
        var full = line.Width is { } w ? Math.Round(w * length, 6, MidpointRounding.AwayFromZero) : length;
        if (quantity == full) return (line.Width, length);
        if (line.Width is { } width and > 0m) {
            var derived = Math.Round(quantity / width, 4, MidpointRounding.AwayFromZero);
            if (Math.Round(width * derived, 6, MidpointRounding.AwayFromZero) == quantity) return (width, derived);
        } else if (Math.Round(quantity, 4, MidpointRounding.AwayFromZero) == quantity) {
            return (null, quantity);
        }

        throw SalesErrors.Bad("measure_partial_invalid", $"\"{line.Description}\" için kısmi iade miktarı ölçülere bölünemiyor; tam miktarı iade edin.");
    }

    private static InvoiceInput ReturnBody(Invoice original, List<(InvoiceLine Line, ReturnLineInput Input)> picked, DateOnly date) {
        var lines = new List<ERP.Application.Invoicing.LineInput>();
        decimal returnedGross = 0m;
        foreach (var (line, l) in picked) {
            var ratio = line.Quantity == 0m ? 0m : l.Quantity / line.Quantity;
            returnedGross += line.Gross * ratio;
            var (width, length) = Dimensions(line, l.Quantity);
            lines.Add(new ERP.Application.Invoicing.LineInput {
                ProductId = line.ProductId, VariantId = line.VariantId, Description = line.Description, Unit = line.Unit, Quantity = l.Quantity, Width = width, Length = length,
                UnitPrice = line.UnitPrice, VatIncluded = line.VatIncluded, DiscountPercent = line.DiscountPercent,
                DiscountAmount = line.DiscountAmount is { } d ? Math.Round(d * ratio, 2, MidpointRounding.AwayFromZero) : null, VatRate = line.VatRate,
                WithholdingCode = line.WithholdingCode, ExemptionCode = line.ExemptionCode, ExemptionReason = line.ExemptionReason, OtvPercent = line.OtvPercent,
                OtvPerUnit = line.OtvPerUnit, SourceLineId = line.Id, Lots = l.Lots?.ToList(), Serials = l.Serials?.ToList()
            });
        }

        decimal? docAmount = original.DocDiscountAmount is { } da && original.Gross > 0m ? Math.Round(da * returnedGross / original.Gross, 2, MidpointRounding.AwayFromZero) : null;
        return new InvoiceInput {
            Kind = InvoiceKind.SalesReturn, BranchId = original.BranchId, CurrentAccountId = original.CurrentAccountId, Date = date, Currency = original.Currency,
            DocDiscountPercent = original.DocDiscountPercent, DocDiscountAmount = docAmount, AffectsStock = false, ReturnOfInvoiceId = original.Id,
            Notes = $"İade: {original.Number}", Lines = lines
        };
    }
}

[Authorize(Permission = SalesPermissions.Return)]
public sealed record CancelReturnCommand(Guid Id, string Reason) : IRequest<ReturnDto>;

public sealed class CancelReturnCommandValidator : AbstractValidator<CancelReturnCommand> {
    public CancelReturnCommandValidator() {
        RuleFor(c => c.Reason).NotEmpty().WithMessage("İptal nedeni zorunludur.").Length(2, 200).WithMessage("İptal nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

/// <summary>İade belgesini iptal eder: taslak iade faturası silinir, stok girişi geri alınır. Kesilmiş iade faturası varsa önce o iptal edilmelidir.</summary>
public sealed class CancelReturnCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ReturnService         returns,
    ReturnMapper          mapper
) : IRequestHandler<CancelReturnCommand, ReturnDto> {
    public async Task<ReturnDto> Handle(CancelReturnCommand request, CancellationToken ct) {
        var head = await db.SalesReturns.AsNoTracking().Where(r => r.Id == request.Id).Select(r => new { r.CurrentAccountId, r.SalesOrderId, r.BranchId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "SalesReturn");
        BranchGuard.Ensure(tenant, head.BranchId);

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            await db.LockCurrentAccountAsync(head.CurrentAccountId, ct);
            if (head.SalesOrderId is { } orderId) await db.LockSalesOrderAsync(orderId, ct);
            var keys = await returns.StockKeysAsync(request.Id, ct);
            if (keys.Count > 0) await db.LockStockAsync(keys, ct);

            var ret = await db.SalesReturns.Include(r => r.Lines).FirstAsync(r => r.Id == request.Id, ct);
            if (ret.Status != SalesReturnStatus.Posted) throw SalesErrors.Conflict("return_not_cancellable", "İade belgesi zaten iptal edilmiş.");

            if (ret.ReturnInvoiceId is { } invoiceId) {
                var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.DueSchedule).FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
                if (invoice is { Status: InvoiceStatus.Issued })
                    throw SalesErrors.Conflict("return_invoice_issued", "İade faturası kesilmiş; önce iade faturasını iptal edin.");
                if (invoice is { Status: InvoiceStatus.Draft }) db.Invoices.Remove(invoice);
            }

            await returns.RevertAsync(ret, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(ret, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw SalesErrors.VersionConflict();
        }
    }
}

[Authorize(Permission = SalesPermissions.Read)]
public sealed record ListReturnsQuery(SalesReturnStatus? Status, Guid? InvoiceId, Guid? CurrentAccountId, int Page = 1, int PageSize = 25) : IRequest<ReturnListResult>;

public sealed class ListReturnsQueryHandler(IApplicationDbContext db, ICurrentTenant tenant) : IRequestHandler<ListReturnsQuery, ReturnListResult> {
    public async Task<ReturnListResult> Handle(ListReturnsQuery request, CancellationToken ct) {
        var query = db.SalesReturns.AsNoTracking().AsQueryable();
        if (tenant.BranchIds is { } branchList) {
            var branches = branchList.ToList();
            query = query.Where(r => branches.Contains(r.BranchId));
        }

        if (request.Status is { } status) query = query.Where(r => r.Status == status);
        if (request.InvoiceId is { } invoice) query = query.Where(r => r.InvoiceId == invoice);
        if (request.CurrentAccountId is { } cari) query = query.Where(r => r.CurrentAccountId == cari);

        var total    = await query.CountAsync(ct);
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var rows = await query.OrderByDescending(r => r.Created).ThenByDescending(r => r.Number).Skip((page - 1) * pageSize).Take(pageSize)
                              .Select(r => new {
                                  r.Id, r.Number, r.Status, r.InvoiceId, InvoiceNumber = db.Invoices.Where(i => i.Id == r.InvoiceId).Select(i => i.Number).FirstOrDefault(), r.CurrentAccountId,
                                  CustomerName = db.CurrentAccounts.Where(c => c.Id == r.CurrentAccountId).Select(c => c.Name).FirstOrDefault(), r.Date
                              }).ToListAsync(ct);
        return new ReturnListResult(rows.Select(r => new ReturnRowDto(r.Id, r.Number, r.Status, r.InvoiceId, r.InvoiceNumber, r.CurrentAccountId, r.CustomerName ?? string.Empty, r.Date)).ToList(), total);
    }
}

[Authorize(Permission = SalesPermissions.Read)]
public sealed record GetReturnQuery(Guid Id) : IRequest<ReturnDto>;

public sealed class GetReturnQueryHandler(IApplicationDbContext db, ICurrentTenant tenant, ReturnMapper mapper) : IRequestHandler<GetReturnQuery, ReturnDto> {
    public async Task<ReturnDto> Handle(GetReturnQuery request, CancellationToken ct) {
        var ret = await db.SalesReturns.AsNoTracking().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == request.Id, ct) ?? throw new NotFoundException(request.Id.ToString(), "SalesReturn");
        BranchGuard.Ensure(tenant, ret.BranchId);
        return await mapper.ToDtoAsync(ret, ct);
    }
}
