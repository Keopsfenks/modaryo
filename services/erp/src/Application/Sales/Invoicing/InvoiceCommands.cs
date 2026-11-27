using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing;
using ERP.Application.Sales.Orders;
using ERP.Application.Sales.Shipments;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Invoicing;

public sealed record InvoiceFromShipmentsInput(IReadOnlyList<Guid> ShipmentIds, DateOnly? Date);

/// <summary>
///     Sevklerden satış faturası taslağı (aynı siparişin sevkleri). Faturalanan miktar sipariş kilidi altında sayılır: eşzamanlı taslaklar
///     aynı sevk miktarını iki kez faturalayamaz.
/// </summary>
[Authorize(Permission = SalesPermissions.Manage)]
[Authorize(Permission = InvoicingPermissions.Manage)]
public sealed record CreateInvoiceFromShipmentsCommand(InvoiceFromShipmentsInput Input) : IRequest<InvoiceDto>;

public sealed class CreateInvoiceFromShipmentsCommandValidator : AbstractValidator<CreateInvoiceFromShipmentsCommand> {
    public CreateInvoiceFromShipmentsCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.ShipmentIds).NotEmpty().WithMessage("En az bir sevk seçilmelidir.");
    }
}

public sealed class CreateInvoiceFromShipmentsCommandHandler(
    IApplicationDbContext  db,
    ICurrentTenant         tenant,
    ShipmentInvoiceBuilder drafts
) : IRequestHandler<CreateInvoiceFromShipmentsCommand, InvoiceDto> {
    public async Task<InvoiceDto> Handle(CreateInvoiceFromShipmentsCommand request, CancellationToken ct) {
        var ids   = request.Input.ShipmentIds.Distinct().ToList();
        var heads = await db.Shipments.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.SalesOrderId, s.BranchId }).ToListAsync(ct);
        if (heads.Count != ids.Count) throw new NotFoundException(string.Join(",", ids.Except(heads.Select(h => h.Id))), "Shipment");
        foreach (var h in heads) BranchGuard.Ensure(tenant, h.BranchId);
        var orderIds = heads.Select(h => h.SalesOrderId).Distinct().ToList();
        if (orderIds.Count != 1) throw SalesErrors.Bad("order_mismatch", "Faturalanacak sevklerin siparişi aynı olmalıdır.");

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            if (!await db.LockSalesOrderAsync(orderIds[0], ct)) throw new NotFoundException(orderIds[0].ToString(), "SalesOrder");
            var (draft, cariName) = await drafts.CreateDraftAsync(ids, request.Input.Date, ct);
            await tx.CommitAsync(ct);
            return InvoiceRules.ToDto(draft, cariName);
        } catch (DbUpdateException ex) when (InvoicingErrors.IsUniqueViolation(ex)) {
            db.ClearTracking();
            throw InvoiceErrors.Conflict("number_conflict", "Fatura kaydı başka bir belgeyle çakıştı; tekrar deneyin.");
        }
    }
}

public sealed record QuickInvoiceInput(Guid? WarehouseId);

public sealed record QuickInvoiceResult(Guid ShipmentId, Guid InvoiceId, InvoiceDto Invoice);

/// <summary>
///     Hızlı fatura: siparişin tüm açık miktarları sevk edilir ve faturalanıp kesilir; tek işlemde. Stok yetersizse (ya da herhangi bir adım
///     başarısızsa) sevk, stok çıkışı ve fatura birlikte geri alınır. Kilit sırası: cari, sipariş, stok.
/// </summary>
[Authorize(Permission = SalesPermissions.Ship)]
[Authorize(Permission = InvoicingPermissions.Issue)]
public sealed record QuickInvoiceCommand(Guid OrderId, Guid? WarehouseId) : IRequest<QuickInvoiceResult>;

public sealed class QuickInvoiceCommandHandler(
    IApplicationDbContext  db,
    ICurrentTenant         tenant,
    ShipmentService        shipments,
    ShipmentInvoiceBuilder drafts,
    InvoiceIssuer          issuer,
    TimeProvider           clock
) : IRequestHandler<QuickInvoiceCommand, QuickInvoiceResult> {
    public async Task<QuickInvoiceResult> Handle(QuickInvoiceCommand request, CancellationToken ct) {
        var head = await OrderRules.LoadAsync(db, tenant, request.OrderId, false, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            await db.LockCurrentAccountAsync(head.CurrentAccountId, ct);
            if (!await db.LockSalesOrderAsync(head.Id, ct)) throw new NotFoundException(head.Id.ToString(), "SalesOrder");
            db.ClearTracking();
            var order = await OrderRules.LoadAsync(db, tenant, head.Id, true, ct);

            var open = ShipmentService.OpenLines(order);
            if (open.Count == 0) throw SalesErrors.Conflict("nothing_to_ship", "Siparişte sevk edilecek açık miktar yok.");

            // Kilit sırası normal kesimle aynı olsun diye (cari, sipariş, seri numarası, stok) seri numarası sevkin stok kilitlerinden önce ayrılır.
            var date      = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
            var cari      = await db.CurrentAccounts.AsNoTracking().FirstAsync(c => c.Id == order.CurrentAccountId, ct);
            var reserved  = await issuer.ReserveNumberAsync(order.BranchId, InvoiceKind.Sales, date, cari, ct);
            var shipment  = await shipments.ShipAsync(order, new ShipmentInput(order.Id, request.WarehouseId, date, null, null, null, open), ct);
            var (draft, _) = await drafts.CreateDraftAsync([shipment.Id], date, ct);
            var (invoice, cariName) = await issuer.IssueAsync(draft.Id, false, ct, reserved);
            await tx.CommitAsync(ct);
            return new QuickInvoiceResult(shipment.Id, invoice.Id, InvoiceRules.ToDto(invoice, cariName));
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw SalesErrors.VersionConflict();
        }
    }
}
