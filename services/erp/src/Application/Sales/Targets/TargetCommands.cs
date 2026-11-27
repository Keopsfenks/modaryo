using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Sales.Invoicing;
using ERP.Application.Sales.Returns;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Targets;

public sealed record TargetRowInput(Guid UserId, int Month, decimal AmountTry);

public sealed record TargetRowDto(Guid UserId, string? UserName, int Month, decimal AmountTry);

public sealed record TargetsDto(int Year, IReadOnlyList<TargetRowDto> Rows);

public sealed record TargetActualRow(Guid UserId, string? UserName, int Month, decimal TargetTry, decimal ActualTry, decimal? Percent);

public sealed record TargetActualsDto(int Year, IReadOnlyList<TargetActualRow> Rows);

[Authorize(Permission = SalesPermissions.Targets)]
public sealed record GetTargetsQuery(int? Year) : IRequest<TargetsDto>;

public sealed class GetTargetsQueryHandler(
    IApplicationDbContext db,
    TimeProvider          clock
) : IRequestHandler<GetTargetsQuery, TargetsDto> {
    public async Task<TargetsDto> Handle(GetTargetsQuery request, CancellationToken ct) {
        var year = request.Year ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3)).Year;
        var rows = await db.SalesTargets.AsNoTracking().Where(t => t.Year == year).OrderBy(t => t.UserName).ThenBy(t => t.UserId).ThenBy(t => t.Month).ToListAsync(ct);
        return new TargetsDto(year, rows.Select(t => new TargetRowDto(t.UserId, t.UserName, t.Month, t.Amount)).ToList());
    }
}

/// <summary>Yılın hedeflerini verilen satırlarla değiştirir (temsilci x ay, TRY). Temsilci firma üyesi olmalıdır.</summary>
[Authorize(Permission = SalesPermissions.Targets)]
public sealed record PutTargetsCommand(int Year, IReadOnlyList<TargetRowInput> Rows) : IRequest<TargetsDto>;

public sealed class PutTargetsCommandValidator : AbstractValidator<PutTargetsCommand> {
    public PutTargetsCommandValidator() {
        RuleFor(c => c.Year).InclusiveBetween(2000, 2100).WithMessage("Yıl 2000 ile 2100 arasında olmalıdır.");
        RuleFor(c => c.Rows).NotNull().Must(r => r is null || r.Count <= 2000).WithMessage("En fazla 2000 satır verilebilir.")
            .Must(r => r is null || r.Select(x => (x.UserId, x.Month)).Distinct().Count() == r.Count).WithMessage("Aynı temsilci ve ay için tek hedef verilebilir.");
        RuleForEach(c => c.Rows).ChildRules(r => {
            r.RuleFor(x => x.UserId).NotEmpty().WithMessage("Temsilci seçilmelidir.");
            r.RuleFor(x => x.Month).InclusiveBetween(1, 12).WithMessage("Ay 1 ile 12 arasında olmalıdır.");
            r.RuleFor(x => x.AmountTry).GreaterThanOrEqualTo(0m).WithMessage("Hedef negatif olamaz.").Must(a => Math.Round(a, 2) == a).WithMessage("Hedef en fazla 2 ondalık basamak içerebilir.");
        });
    }
}

public sealed class PutTargetsCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IMemberDirectory      members,
    TimeProvider          clock
) : IRequestHandler<PutTargetsCommand, TargetsDto> {
    public async Task<TargetsDto> Handle(PutTargetsCommand request, CancellationToken ct) {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var names    = new Dictionary<Guid, string?>();
        foreach (var userId in request.Rows.Select(r => r.UserId).Distinct()) {
            var member = await members.GetAsync(tenantId, userId, ct) ?? throw SalesErrors.Bad("sales_rep_invalid", "Satış temsilcisi bu firmanın üyesi değil.");
            names[userId] = member.FullName;
        }

        await using var tx = await db.BeginTransactionAsync(ct);
        db.SalesTargets.RemoveRange(await db.SalesTargets.Where(t => t.Year == request.Year).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        foreach (var row in request.Rows) {
            db.SalesTargets.Add(new SalesTarget { UserId = row.UserId, UserName = names[row.UserId], Year = request.Year, Month = row.Month, Amount = row.AmountTry });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await new GetTargetsQueryHandler(db, clock).Handle(new GetTargetsQuery(request.Year), ct);
    }
}

/// <summary>
///     Hedef ve gerçekleşen (TRY). Gerçekleşen = kesilmiş satış faturalarının KDV hariç net tutarı (TL) - kesilmiş iade faturaları, fatura tarihinin ayına göre.
///     Faturanın temsilcisi: sevkten kesilmişse siparişin temsilcisi; iade belgesinden ya da iade faturasından asıl faturanın temsilcisi; diğer faturalarda carinin varsayılan temsilcisi.
///     Temsilcisi belirlenemeyen faturalar hiçbir temsilciye yazılmaz.
/// </summary>
[Authorize(Permission = SalesPermissions.Targets)]
public sealed record GetTargetActualsQuery(int? Year) : IRequest<TargetActualsDto>;

public sealed class GetTargetActualsQueryHandler(
    IApplicationDbContext db,
    TimeProvider          clock
) : IRequestHandler<GetTargetActualsQuery, TargetActualsDto> {
    private sealed record Row(Guid Id, InvoiceKind Kind, string SourceType, Guid? SourceId, Guid? ReturnOf, Guid CurrentAccountId, DateOnly Date, decimal NetTl);

    public async Task<TargetActualsDto> Handle(GetTargetActualsQuery request, CancellationToken ct) {
        var year  = request.Year ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3)).Year;
        var from  = new DateOnly(year, 1, 1);
        var to    = new DateOnly(year, 12, 31);
        var kinds = new[] { InvoiceKind.Sales, InvoiceKind.SalesReturn };

        Row Map(Invoice i) => new(i.Id, i.Kind, i.SourceType, i.SourceId, i.ReturnOfInvoiceId, i.CurrentAccountId, i.Date, i.NetTl);
        var rows = (await db.Invoices.AsNoTracking().Where(i => i.Status == InvoiceStatus.Issued && kinds.Contains(i.Kind) && i.Date >= from && i.Date <= to).ToListAsync(ct)).Select(Map).ToList();

        // Asıl faturalar (başka yıldan olabilir) temsilcisi için yüklenir.
        var knownIds    = rows.Select(r => r.Id).ToHashSet();
        var originalIds = rows.Where(r => r.ReturnOf is { } o && !knownIds.Contains(o)).Select(r => r.ReturnOf!.Value).Distinct().ToList();
        var all         = rows.ToDictionary(r => r.Id);
        if (originalIds.Count > 0)
            foreach (var original in await db.Invoices.AsNoTracking().Where(i => originalIds.Contains(i.Id)).ToListAsync(ct)) all[original.Id] = Map(original);

        var shipmentIds = all.Values.Where(r => r.SourceType == ShipmentInvoiceLinks.Source && r.SourceId != null).Select(r => r.SourceId!.Value).Distinct().ToList();
        var returnIds   = all.Values.Where(r => r.SourceType == ReturnService.Source && r.SourceId != null).Select(r => r.SourceId!.Value).Distinct().ToList();
        var shipmentOrder = await db.Shipments.AsNoTracking().Where(s => shipmentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => (Guid?)s.SalesOrderId, ct);
        var returnOrder   = await db.SalesReturns.AsNoTracking().Where(s => returnIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.SalesOrderId, ct);
        var orderIds      = shipmentOrder.Values.Concat(returnOrder.Values).Where(o => o != null).Select(o => o!.Value).Distinct().ToList();
        var orders        = await db.SalesOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => (o.SalesRepUserId, o.SalesRepName), ct);
        var cariIds       = all.Values.Select(r => r.CurrentAccountId).Distinct().ToList();
        var cariReps      = await db.CurrentAccounts.AsNoTracking().Where(c => cariIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.SalesRepUserId, ct);

        var names = new Dictionary<Guid, string?>();
        Guid? RepOf(Row row, int depth = 0) {
            Guid? orderId = null;
            if (row.SourceType == ShipmentInvoiceLinks.Source && row.SourceId is { } shipment) orderId = shipmentOrder.GetValueOrDefault(shipment);
            else if (row.SourceType == ReturnService.Source && row.SourceId is { } ret) orderId = returnOrder.GetValueOrDefault(ret);

            if (orderId is { } order && orders.TryGetValue(order, out var rep) && rep.SalesRepUserId is { } user) {
                if (rep.SalesRepName is { } name) names.TryAdd(user, name);
                return user;
            }

            if (row.ReturnOf is { } original && depth < 2 && all.TryGetValue(original, out var originalRow) && RepOf(originalRow, depth + 1) is { } inherited) return inherited;
            return cariReps.GetValueOrDefault(row.CurrentAccountId);
        }

        var actual = new Dictionary<(Guid UserId, int Month), decimal>();
        foreach (var row in rows) {
            if (RepOf(row) is not { } user) continue;
            var key = (user, row.Date.Month);
            actual[key] = actual.GetValueOrDefault(key) + (row.Kind == InvoiceKind.SalesReturn ? -row.NetTl : row.NetTl);
        }

        var targets = await db.SalesTargets.AsNoTracking().Where(t => t.Year == year).ToListAsync(ct);
        foreach (var target in targets) {
            if (target.UserName is { } name) names[target.UserId] = name;
        }

        var keys = targets.Select(t => (t.UserId, t.Month)).Concat(actual.Keys).Distinct().ToList();
        var list = keys.Select(k => {
            var target = targets.FirstOrDefault(t => t.UserId == k.UserId && t.Month == k.Month)?.Amount ?? 0m;
            var done   = actual.GetValueOrDefault(k);
            return new TargetActualRow(k.UserId, names.GetValueOrDefault(k.UserId), k.Month, target, done, target > 0m ? Math.Round(done / target * 100m, 1, MidpointRounding.AwayFromZero) : null);
        }).OrderBy(r => r.UserName).ThenBy(r => r.UserId).ThenBy(r => r.Month).ToList();
        return new TargetActualsDto(year, list);
    }
}
