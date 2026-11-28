using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Pos.Shifts;
using ERP.Domain.Pos;

namespace ERP.Application.Pos.Reports;

public sealed record CashierSalesDto(Guid CashierUserId, string? CashierName, int SalesCount, decimal SalesTotal, int CancelledCount, int ReturnsCount, decimal ReturnsTotal);

/// <summary>Gün sonu: kasa ve gün başına tüm vardiyaların toplamı. Ödeme yöntemleri net nakit (para üstü düşülmüş), kart, veresiye ve iade mahsubudur.</summary>
public sealed record DayReportDto(
    Guid                           RegisterId,
    DateOnly                       Date,
    int                            SalesCount,
    decimal                        SalesTotal,
    decimal                        CashNet,
    decimal                        Card,
    decimal                        OnAccount,
    decimal                        ReturnCredit,
    decimal                        ChangeGiven,
    int                            CancelledCount,
    decimal                        CancelledTotal,
    int                            ReturnsCount,
    decimal                        ReturnsTotal,
    decimal                        CashRefunds,
    decimal                        CardRefunds,
    decimal                        CampaignDiscount,
    decimal                        NetSales,
    IReadOnlyList<VatLineDto>      VatSummary,
    IReadOnlyList<VatLineDto>      ReturnVatSummary,
    IReadOnlyList<CashierSalesDto> Cashiers,
    string?                        FirstReceipt,
    string?                        LastReceipt
);

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Reports)]
public sealed record DayReportQuery(Guid RegisterId, DateOnly Date) : IRequest<DayReportDto>;

public sealed class DayReportQueryHandler(IApplicationDbContext db, ICurrentTenant tenant) : IRequestHandler<DayReportQuery, DayReportDto> {
    public async Task<DayReportDto> Handle(DayReportQuery request, CancellationToken ct) {
        var register = await db.PosRegisters.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RegisterId, ct) ?? throw new NotFoundException(request.RegisterId.ToString(), "Register");
        BranchGuard.Ensure(tenant, register.BranchId);

        var sales     = await db.PosSales.AsNoTracking().Include(s => s.Lines).Where(s => s.RegisterId == register.Id && s.Date == request.Date).OrderBy(s => s.Number).ToListAsync(ct);
        var returns   = await db.PosReturns.AsNoTracking().Include(r => r.Lines).Where(r => r.RegisterId == register.Id && r.Date == request.Date).ToListAsync(ct);
        var done      = sales.Where(s => s.Status == PosSaleStatus.Completed).ToList();
        var cancelled = sales.Where(s => s.Status == PosSaleStatus.Cancelled).ToList();

        var vat = done.SelectMany(s => s.Lines).GroupBy(l => l.VatRate).OrderBy(g => g.Key).Select(g => new VatLineDto(g.Key, g.Sum(l => l.Net), g.Sum(l => l.Vat))).ToList();
        var returnLineIds = returns.SelectMany(r => r.Lines).Select(x => x.SaleLineId).Distinct().ToList();
        var rates     = await db.PosSaleLines.AsNoTracking().Where(l => returnLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.VatRate, ct);
        var returnVat = returns.SelectMany(r => r.Lines).GroupBy(l => rates.GetValueOrDefault(l.SaleLineId)).OrderBy(g => g.Key).Select(g => new VatLineDto(g.Key, g.Sum(l => l.Net), g.Sum(l => l.Vat))).ToList();

        var cashierIds = sales.Select(s => s.CashierUserId).Concat(returns.Select(r => r.CashierUserId)).Distinct();
        var cashiers = cashierIds.Select(u => {
            var mine = sales.Where(s => s.CashierUserId == u).ToList();
            var ret  = returns.Where(r => r.CashierUserId == u).ToList();
            return new CashierSalesDto(u, mine.Select(s => s.CashierName).FirstOrDefault(n => n != null) ?? ret.Select(r => r.CashierName).FirstOrDefault(n => n != null),
                                       mine.Count(s => s.Status == PosSaleStatus.Completed), mine.Where(s => s.Status == PosSaleStatus.Completed).Sum(s => s.Total),
                                       mine.Count(s => s.Status == PosSaleStatus.Cancelled), ret.Count, ret.Sum(r => r.Net + r.Vat));
        }).ToList();

        var salesTotal   = done.Sum(s => s.Total);
        var returnsTotal = returns.Sum(r => r.Net + r.Vat);
        return new DayReportDto(register.Id, request.Date, done.Count, salesTotal, done.Sum(s => s.CashNet), done.Sum(s => s.CardAmount), done.Sum(s => s.OnAccountAmount), done.Sum(s => s.ReturnCreditAmount),
                                done.Sum(s => s.ChangeGiven), cancelled.Count, cancelled.Sum(s => s.Total), returns.Count, returnsTotal, returns.Sum(r => r.CashRefund), returns.Sum(r => r.CardRefund),
                                done.SelectMany(s => s.Lines).Sum(l => l.CampaignDiscount), salesTotal - returnsTotal, vat, returnVat, cashiers, sales.FirstOrDefault()?.Number, sales.LastOrDefault()?.Number);
    }
}

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Reports)]
public sealed record CashierReportQuery(DateOnly From, DateOnly To, Guid? RegisterId) : IRequest<IReadOnlyList<CashierSalesDto>>;

public sealed class CashierReportQueryValidator : AbstractValidator<CashierReportQuery> {
    public CashierReportQueryValidator() {
        RuleFor(q => q).Must(q => q.To >= q.From && q.To.DayNumber - q.From.DayNumber <= 366).WithMessage("Tarih aralığı geçersiz ya da 1 yıldan uzun.");
    }
}

public sealed class CashierReportQueryHandler(IApplicationDbContext db, ICurrentTenant tenant) : IRequestHandler<CashierReportQuery, IReadOnlyList<CashierSalesDto>> {
    public async Task<IReadOnlyList<CashierSalesDto>> Handle(CashierReportQuery request, CancellationToken ct) {
        var registers = (await db.PosRegisters.AsNoTracking().ToListAsync(ct)).Where(r => tenant.CanAccessBranch(r.BranchId)).Select(r => r.Id).ToList();
        var sales     = db.PosSales.AsNoTracking().Where(s => s.Date >= request.From && s.Date <= request.To && registers.Contains(s.RegisterId));
        var returns   = db.PosReturns.AsNoTracking().Where(r => r.Date >= request.From && r.Date <= request.To && registers.Contains(r.RegisterId));
        if (request.RegisterId is { } id) {
            sales   = sales.Where(s => s.RegisterId == id);
            returns = returns.Where(r => r.RegisterId == id);
        }

        var saleRows   = await sales.Select(s => new { s.CashierUserId, s.CashierName, s.Status, s.Total }).ToListAsync(ct);
        var returnRows = await returns.Select(r => new { r.CashierUserId, r.Net, r.Vat }).ToListAsync(ct);
        return saleRows.Select(s => s.CashierUserId).Concat(returnRows.Select(r => r.CashierUserId)).Distinct().Select(u => new CashierSalesDto(
                   u, saleRows.Where(s => s.CashierUserId == u).Select(s => s.CashierName).FirstOrDefault(n => n != null),
                   saleRows.Count(s => s.CashierUserId == u && s.Status == PosSaleStatus.Completed), saleRows.Where(s => s.CashierUserId == u && s.Status == PosSaleStatus.Completed).Sum(s => s.Total),
                   saleRows.Count(s => s.CashierUserId == u && s.Status == PosSaleStatus.Cancelled), returnRows.Count(r => r.CashierUserId == u),
                   returnRows.Where(r => r.CashierUserId == u).Sum(r => r.Net + r.Vat)))
               .OrderByDescending(c => c.SalesTotal).ToList();
    }
}
