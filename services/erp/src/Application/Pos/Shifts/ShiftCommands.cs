using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Pos;

namespace ERP.Application.Pos.Shifts;

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Shift)]
public sealed record OpenShiftCommand(Guid RegisterId, decimal OpeningCash) : IRequest<ShiftDto>;

public sealed class OpenShiftCommandValidator : AbstractValidator<OpenShiftCommand> {
    public OpenShiftCommandValidator() {
        RuleFor(c => c.RegisterId).NotEmpty().WithMessage("Kasa seçilmelidir.");
        RuleFor(c => c.OpeningCash).GreaterThanOrEqualTo(0m).WithMessage("Açılış nakdi negatif olamaz.")
                                   .Must(v => Math.Round(v, 2) == v).WithMessage("Açılış nakdi en fazla 2 ondalık basamak içerebilir.");
    }
}

/// <summary>
///     Vardiya açar: kasanın aynı anda tek açık vardiyası olur (kısmi tekil dizin yarışta da korur). Kasiyer farklı kasalarda ayrı vardiyalar açabilir
///     ama bir kasada yalnızca bir kez.
/// </summary>
public sealed class OpenShiftCommandHandler(IApplicationDbContext db, ICurrentTenant tenant, ICurrentSession session, TimeProvider time) : IRequestHandler<OpenShiftCommand, ShiftDto> {
    public async Task<ShiftDto> Handle(OpenShiftCommand request, CancellationToken ct) {
        var register = await db.PosRegisters.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RegisterId, ct) ?? throw new NotFoundException(request.RegisterId.ToString(), "Register");
        BranchGuard.Ensure(tenant, register.BranchId);
        if (!register.IsActive) throw PosErrors.RegisterInactive();
        var userId = session.UserId ?? throw new ForbiddenAccessException();

        if (await db.PosShifts.AnyAsync(s => s.RegisterId == register.Id && s.Status == ShiftStatus.Open, ct)) throw AlreadyOpen();

        var shift = new Shift { RegisterId = register.Id, CashierUserId = userId, OpenedAt = time.GetUtcNow(), OpeningCash = request.OpeningCash };
        db.PosShifts.Add(shift);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (PosErrors.IsUniqueViolation(ex)) {
            db.ClearTracking();
            throw AlreadyOpen();
        }

        return ShiftRules.ToDto(shift, register.Code);
    }

    private static AppException AlreadyOpen() {
        return PosErrors.Conflict("shift_already_open", "Bu kasada zaten açık bir vardiya var; önce kapatılmalıdır.");
    }
}

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Shift)]
public sealed record CloseShiftCommand(Guid Id, decimal CountedCash, string? Note) : IRequest<ShiftReportDto>;

public sealed class CloseShiftCommandValidator : AbstractValidator<CloseShiftCommand> {
    public CloseShiftCommandValidator() {
        RuleFor(c => c.CountedCash).GreaterThanOrEqualTo(0m).WithMessage("Sayılan nakit negatif olamaz.")
                                   .Must(v => Math.Round(v, 2) == v).WithMessage("Sayılan nakit en fazla 2 ondalık basamak içerebilir.");
        RuleFor(c => c.Note).MaximumLength(500).WithMessage("Not en fazla 500 karakter olabilir.");
    }
}

/// <summary>
///     Vardiyayı kapatır: vardiya kilidi altında beklenen nakit hesaplanır, sayılan nakitle farkı saklanır. Kilit, eşzamanlı satış/iade/kasa hareketinin
///     kapanıştan sonra yazılmasını engeller (sonraki satış shift_required alır).
/// </summary>
public sealed class CloseShiftCommandHandler(IApplicationDbContext db, ICurrentTenant tenant, ICurrentSession session, TimeProvider time) : IRequestHandler<CloseShiftCommand, ShiftReportDto> {
    public async Task<ShiftReportDto> Handle(CloseShiftCommand request, CancellationToken ct) {
        var head = await db.PosShifts.AsNoTracking().Where(s => s.Id == request.Id).Select(s => new { s.RegisterId }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException(request.Id.ToString(), "Shift");
        var register = await db.PosRegisters.AsNoTracking().FirstAsync(r => r.Id == head.RegisterId, ct);
        BranchGuard.Ensure(tenant, register.BranchId);

        try {
            await using var tx = await db.BeginTransactionAsync(ct);
            if (!await db.LockShiftAsync(request.Id, ct)) throw new NotFoundException(request.Id.ToString(), "Shift");

            var shift = await db.PosShifts.FirstAsync(s => s.Id == request.Id, ct);
            ShiftRules.EnsureOwnerOrManager(shift, session);
            if (shift.Status != ShiftStatus.Open) throw ShiftRules.Closed();

            var totals = await ShiftRules.TotalsAsync(db, shift, ct);
            shift.Status       = ShiftStatus.Closed;
            shift.ClosedAt     = time.GetUtcNow();
            shift.CountedCash  = request.CountedCash;
            shift.ExpectedCash = totals.ExpectedCash;
            shift.Difference   = request.CountedCash - totals.ExpectedCash;
            shift.CloseNote    = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await ShiftRules.ReportAsync(db, shift, register.Code, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw PosErrors.VersionConflict();
        }
    }
}

public sealed record CashMoveResultDto(CashMoveDto Move, decimal ExpectedCash, IReadOnlyList<string> Warnings);

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.CashMove)]
public sealed record AddCashMoveCommand(Guid ShiftId, CashMoveDirection Kind, decimal Amount, string Reason) : IRequest<CashMoveResultDto>;

public sealed class AddCashMoveCommandValidator : AbstractValidator<AddCashMoveCommand> {
    public AddCashMoveCommandValidator() {
        RuleFor(c => c.Kind).IsInEnum().WithMessage("Hareket yönü giriş ya da çıkış olmalıdır.");
        RuleFor(c => c.Amount).GreaterThan(0m).WithMessage("Tutar sıfırdan büyük olmalıdır.").Must(v => Math.Round(v, 2) == v).WithMessage("Tutar en fazla 2 ondalık basamak içerebilir.");
        RuleFor(c => c.Reason).Must(r => !string.IsNullOrWhiteSpace(r) && r.Trim().Length <= 500).WithMessage("Neden zorunludur ve en fazla 500 karakter olabilir.");
    }
}

/// <summary>
///     Vardiya içi kasa girişi/çıkışı (avans, para alma). Yalnızca vardiya beklenen nakdini etkiler; muhasebe karşılığı belirsiz olduğundan hesap hareketi
///     yazmaz (karşı hesap için virman/masraf fişi kullanılır). Çıkış beklenen nakdi eksiye düşürürse ayara göre uyarı (cash_negative) ya da 409 verir.
/// </summary>
public sealed class AddCashMoveCommandHandler(IApplicationDbContext db, ICurrentTenant tenant, ICurrentSession session, ITenantSettings settings, TimeProvider time)
    : IRequestHandler<AddCashMoveCommand, CashMoveResultDto> {
    public async Task<CashMoveResultDto> Handle(AddCashMoveCommand request, CancellationToken ct) {
        var head     = await db.PosShifts.AsNoTracking().Where(s => s.Id == request.ShiftId).Select(s => new { s.RegisterId }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException(request.ShiftId.ToString(), "Shift");
        var register = await db.PosRegisters.AsNoTracking().FirstAsync(r => r.Id == head.RegisterId, ct);
        BranchGuard.Ensure(tenant, register.BranchId);
        var pos = await settings.GetAsync<PosSettings>(ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockShiftAsync(request.ShiftId, ct)) throw new NotFoundException(request.ShiftId.ToString(), "Shift");
        var shift = await db.PosShifts.AsNoTracking().FirstAsync(s => s.Id == request.ShiftId, ct);
        ShiftRules.EnsureOwnerOrManager(shift, session);
        if (shift.Status != ShiftStatus.Open) throw ShiftRules.Closed();

        var warnings = new List<string>();
        var before   = (await ShiftRules.TotalsAsync(db, shift, ct)).ExpectedCash;
        var after    = request.Kind == CashMoveDirection.Out ? before - request.Amount : before + request.Amount;
        if (after < 0m) {
            if (!pos.AllowNegativeCashWarningOnly) throw PosErrors.Conflict("cash_negative", "Kasada yeterli nakit yok.");
            warnings.Add("cash_negative");
        }

        var move = new CashMove { ShiftId = shift.Id, Direction = request.Kind, Amount = request.Amount, Note = request.Reason.Trim(), At = time.GetUtcNow(), CreatedByUserId = session.UserId };
        db.PosCashMoves.Add(move);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new CashMoveResultDto(new CashMoveDto(move.Id, move.Direction, move.Amount, move.Note, move.At, move.CreatedByUserId), after, warnings);
    }
}

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.ViewShifts)]
public sealed record ListShiftsQuery(Guid? RegisterId, ShiftStatus? Status, int Limit = 50) : IRequest<IReadOnlyList<ShiftDto>>;

public sealed class ListShiftsQueryHandler(IApplicationDbContext db, ICurrentTenant tenant, ICurrentSession session) : IRequestHandler<ListShiftsQuery, IReadOnlyList<ShiftDto>> {
    public async Task<IReadOnlyList<ShiftDto>> Handle(ListShiftsQuery request, CancellationToken ct) {
        var registers = (await db.PosRegisters.AsNoTracking().ToListAsync(ct)).Where(r => tenant.CanAccessBranch(r.BranchId)).ToDictionary(r => r.Id, r => r.Code);
        var query     = db.PosShifts.AsNoTracking().Where(s => registers.Keys.Contains(s.RegisterId));
        if (request.RegisterId is { } register) query = query.Where(s => s.RegisterId == register);
        if (request.Status is { } status) query = query.Where(s => s.Status == status);

        // Yönetici ya da rapor yetkisi olmayan kasiyer yalnızca kendi vardiyalarını görür.
        if (!session.HasPermission(PosPermissions.Manage) && !session.HasPermission(PosPermissions.Reports) && session.UserId is { } me) query = query.Where(s => s.CashierUserId == me);

        var rows = await query.OrderByDescending(s => s.OpenedAt).Take(Math.Clamp(request.Limit, 1, 200)).ToListAsync(ct);
        return rows.Select(s => ShiftRules.ToDto(s, registers[s.RegisterId])).ToList();
    }
}

/// <summary>Kasiyerin bu kasadaki açık vardiyası; yoksa 404.</summary>
[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.ViewRegisters)]
public sealed record CurrentShiftQuery(Guid RegisterId) : IRequest<ShiftDto>;

public sealed class CurrentShiftQueryHandler(IApplicationDbContext db, ICurrentTenant tenant, ICurrentSession session) : IRequestHandler<CurrentShiftQuery, ShiftDto> {
    public async Task<ShiftDto> Handle(CurrentShiftQuery request, CancellationToken ct) {
        var register = await db.PosRegisters.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RegisterId, ct) ?? throw new NotFoundException(request.RegisterId.ToString(), "Register");
        BranchGuard.Ensure(tenant, register.BranchId);
        var me    = session.UserId ?? throw new ForbiddenAccessException();
        var shift = await db.PosShifts.AsNoTracking().FirstOrDefaultAsync(s => s.RegisterId == register.Id && s.CashierUserId == me && s.Status == ShiftStatus.Open, ct)
                    ?? throw new NotFoundException(request.RegisterId.ToString(), "Shift");
        return ShiftRules.ToDto(shift, register.Code);
    }
}

/// <summary>Vardiya raporu ("Kasa raporu"): sahibi ya da kasa yöneticisi/raporcusu görür.</summary>
[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.ViewShifts)]
public sealed record ShiftReportQuery(Guid Id) : IRequest<ShiftReportDto>;

public sealed class ShiftReportQueryHandler(IApplicationDbContext db, ICurrentTenant tenant, ICurrentSession session) : IRequestHandler<ShiftReportQuery, ShiftReportDto> {
    public async Task<ShiftReportDto> Handle(ShiftReportQuery request, CancellationToken ct) {
        var shift    = await db.PosShifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.Id, ct) ?? throw new NotFoundException(request.Id.ToString(), "Shift");
        var register = await db.PosRegisters.AsNoTracking().FirstAsync(r => r.Id == shift.RegisterId, ct);
        BranchGuard.Ensure(tenant, register.BranchId);
        if (shift.CashierUserId != session.UserId && !session.HasPermission(PosPermissions.Manage) && !session.HasPermission(PosPermissions.Reports)) throw new ForbiddenAccessException();
        return await ShiftRules.ReportAsync(db, shift, register.Code, ct);
    }
}
