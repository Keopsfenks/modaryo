using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Pos;

namespace ERP.Application.Pos.Shifts;

public sealed record ShiftDto(
    Guid           Id,
    Guid           RegisterId,
    string         RegisterCode,
    Guid           CashierUserId,
    ShiftStatus    Status,
    DateTimeOffset OpenedAt,
    decimal        OpeningCash,
    DateTimeOffset? ClosedAt,
    decimal?       CountedCash,
    decimal?       ExpectedCash,
    decimal?       Difference,
    string?        CloseNote,
    uint           Version
);

public sealed record CashMoveDto(Guid Id, CashMoveDirection Kind, decimal Amount, string? Reason, DateTimeOffset At, Guid? CreatedByUserId);

public sealed record VatLineDto(int Rate, decimal Base, decimal Vat);

/// <summary>
///     Vardiya toplamları ("Kasa raporu"). Beklenen nakit = açılış + net nakit satış (para üstü düşülmüş) - nakit iade + kasa girişi - kasa çıkışı.
///     Yalnızca tamamlanmış satışlar sayılır; iptal edilenler ayrı gösterilir. Açık vardiyada beklenen nakit canlı hesaplanır.
/// </summary>
public sealed record ShiftTotals(
    int     SalesCount,
    decimal SalesTotal,
    decimal CashSalesNet,
    decimal CardSales,
    decimal OnAccountSales,
    decimal ReturnCreditSales,
    decimal ChangeGiven,
    int     CancelledCount,
    decimal CancelledTotal,
    int     ReturnsCount,
    decimal ReturnsTotal,
    decimal CashRefunds,
    decimal CardRefunds,
    decimal CashIn,
    decimal CashOut,
    decimal CampaignDiscount,
    decimal ExpectedCash
);

public sealed record ShiftReportDto(
    ShiftDto                  Shift,
    ShiftTotals               Totals,
    IReadOnlyList<VatLineDto> VatSummary,
    string?                   FirstReceipt,
    string?                   LastReceipt,
    IReadOnlyList<CashMoveDto> CashMoves
);

public static class ShiftRules {
    public static ShiftDto ToDto(Shift s, string registerCode) {
        return new ShiftDto(s.Id, s.RegisterId, registerCode, s.CashierUserId, s.Status, s.OpenedAt, s.OpeningCash, s.ClosedAt, s.CountedCash, s.ExpectedCash, s.Difference, s.CloseNote, s.Version);
    }

    public static AppException Closed() {
        return PosErrors.Conflict("shift_closed", "Vardiya kapalı.");
    }

    /// <summary>Vardiya yalnızca sahibi (kasiyer) ya da kasa yöneticisi tarafından kapatılır/hareket alır.</summary>
    public static void EnsureOwnerOrManager(Shift shift, ICurrentSession session) {
        if (shift.CashierUserId != session.UserId && !session.HasPermission(PosPermissions.Manage)) throw new ForbiddenAccessException();
    }

    public static async Task<ShiftTotals> TotalsAsync(IApplicationDbContext db, Shift shift, CancellationToken ct) {
        var sales = await db.PosSales.AsNoTracking().Where(s => s.ShiftId == shift.Id)
                            .Select(s => new { s.Status, s.Total, s.CashNet, s.CardAmount, s.OnAccountAmount, s.ReturnCreditAmount, s.ChangeGiven }).ToListAsync(ct);
        var done      = sales.Where(s => s.Status == PosSaleStatus.Completed).ToList();
        var cancelled = sales.Where(s => s.Status == PosSaleStatus.Cancelled).ToList();
        var returns   = await db.PosReturns.AsNoTracking().Where(r => r.ShiftId == shift.Id).Select(r => new { r.Net, r.Vat, r.CashRefund, r.CardRefund }).ToListAsync(ct);
        var moves     = await db.PosCashMoves.AsNoTracking().Where(m => m.ShiftId == shift.Id).Select(m => new { m.Direction, m.Amount }).ToListAsync(ct);
        var campaign  = await db.PosSales.AsNoTracking().Where(s => s.ShiftId == shift.Id && s.Status == PosSaleStatus.Completed).SelectMany(s => s.Lines).SumAsync(l => (decimal?)l.CampaignDiscount, ct) ?? 0m;

        var cashIn  = moves.Where(m => m.Direction == CashMoveDirection.In).Sum(m => m.Amount);
        var cashOut = moves.Where(m => m.Direction == CashMoveDirection.Out).Sum(m => m.Amount);
        var cashNet = done.Sum(s => s.CashNet);
        var refunds = returns.Sum(r => r.CashRefund);
        return new ShiftTotals(done.Count, done.Sum(s => s.Total), cashNet, done.Sum(s => s.CardAmount), done.Sum(s => s.OnAccountAmount), done.Sum(s => s.ReturnCreditAmount),
                               done.Sum(s => s.ChangeGiven), cancelled.Count, cancelled.Sum(s => s.Total), returns.Count, returns.Sum(r => r.Net + r.Vat), refunds, returns.Sum(r => r.CardRefund),
                               cashIn, cashOut, campaign, ShiftMath.ExpectedCash(shift.OpeningCash, cashNet, refunds, cashIn, cashOut));
    }

    public static async Task<IReadOnlyList<VatLineDto>> VatAsync(IApplicationDbContext db, Guid shiftId, CancellationToken ct) {
        var lines = await db.PosSales.AsNoTracking().Where(s => s.ShiftId == shiftId && s.Status == PosSaleStatus.Completed).SelectMany(s => s.Lines)
                            .Select(l => new { l.VatRate, l.Net, l.Vat }).ToListAsync(ct);
        return lines.GroupBy(l => l.VatRate).OrderBy(g => g.Key).Select(g => new VatLineDto(g.Key, g.Sum(l => l.Net), g.Sum(l => l.Vat))).ToList();
    }

    public static async Task<ShiftReportDto> ReportAsync(IApplicationDbContext db, Shift shift, string registerCode, CancellationToken ct) {
        var totals = await TotalsAsync(db, shift, ct);
        var vat    = await VatAsync(db, shift.Id, ct);
        var numbers = await db.PosSales.AsNoTracking().Where(s => s.ShiftId == shift.Id).OrderBy(s => s.At).ThenBy(s => s.Number).Select(s => s.Number).ToListAsync(ct);
        var moves = (await db.PosCashMoves.AsNoTracking().Where(m => m.ShiftId == shift.Id).OrderBy(m => m.At).ToListAsync(ct))
                    .Select(m => new CashMoveDto(m.Id, m.Direction, m.Amount, m.Note, m.At, m.CreatedByUserId)).ToList();
        return new ShiftReportDto(ToDto(shift, registerCode), totals, vat, numbers.FirstOrDefault(), numbers.LastOrDefault(), moves);
    }
}
