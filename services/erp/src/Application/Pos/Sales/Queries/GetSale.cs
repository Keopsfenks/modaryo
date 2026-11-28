using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.Pos.Sales.Queries;

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.ViewSales)]
public sealed record GetSaleQuery(Guid Id) : IRequest<SaleDto>;

/// <summary>Fiş numarasıyla satış sorgusu (iade ekranı): numara ya da fiş barkodu.</summary>
[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.ViewSales)]
public sealed record GetSaleByNumberQuery(string Number) : IRequest<SaleDto>;

public sealed class GetSaleQueryHandler(IApplicationDbContext db, ICurrentTenant tenant, PosSalePoster poster)
    : IRequestHandler<GetSaleQuery, SaleDto>, IRequestHandler<GetSaleByNumberQuery, SaleDto> {
    public Task<SaleDto> Handle(GetSaleQuery request, CancellationToken ct) {
        return LoadAsync(db.PosSales.AsNoTracking().Where(s => s.Id == request.Id), request.Id.ToString(), ct);
    }

    public Task<SaleDto> Handle(GetSaleByNumberQuery request, CancellationToken ct) {
        var number = request.Number.Trim().ToUpperInvariant();
        return LoadAsync(db.PosSales.AsNoTracking().Where(s => s.Number == number), request.Number, ct);
    }

    private async Task<SaleDto> LoadAsync(IQueryable<ERP.Domain.Pos.PosSale> query, string key, CancellationToken ct) {
        var sale = await query.Include(s => s.Lines).Include(s => s.Payments).FirstOrDefaultAsync(ct) ?? throw new NotFoundException(key, "PosSale");
        BranchGuard.Ensure(tenant, sale.BranchId);
        var slip   = await poster.SlipAsync(sale, ct);
        var number = sale.InvoiceId is { } id ? await db.Invoices.AsNoTracking().Where(i => i.Id == id).Select(i => i.Number).FirstOrDefaultAsync(ct) : null;
        return new SaleDto(sale.Id, sale.Number, sale.Status, sale.RegisterId, sale.ShiftId, sale.CurrentAccountId, sale.Date, sale.At, sale.Mode, sale.Subtotal, sale.Discount, sale.VatTotal,
                           sale.Total, sale.ChangeGiven, sale.InvoiceId, number, sale.CancelReason,
                           sale.Lines.OrderBy(l => l.Sort).Select(l => new SaleLineDto(l.Id, l.Sort, l.ProductId, l.VariantId, l.Description, l.Unit, l.Quantity, l.UnitPrice, l.VatRate, l.Discount,
                                                                                         l.CampaignDiscount, l.Net, l.Vat, l.Total, l.ReturnedQuantity)).ToList(),
                           sale.Payments.OrderBy(p => p.Method).Select(p => new SlipPaymentDto(p.Method, p.Amount)).ToList(), slip);
    }
}
