using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.Treasury.Allocations.Queries;

/// <summary>
///     Eşleştirme seçicisi için vade satırı. Open: tüm eşleştirmeler düşüldükten sonra kalan; Allocatable: elle eşleştirilebilecek tutar
///     (satır tutarı - elle eşleştirmeler; otomatik eşleştirmeler elle olanın önüne geçmez).
/// </summary>
public sealed record OpenItemForAllocationDto(
    Guid              DueLineId,
    Guid              EntryId,
    LedgerEntrySource Source,
    Guid?             SourceId,
    string?           DocumentNo,
    string?           Description,
    DateOnly          Date,
    DateOnly          DueDate,
    int               Sequence,
    DueDirection      Direction,
    decimal           Amount,
    decimal           Open,
    decimal           Allocatable
);

[Authorize]
public sealed record OpenItemsForAllocationQuery(Guid CurrentAccountId) : IRequest<IReadOnlyList<OpenItemForAllocationDto>>;

public sealed class OpenItemsForAllocationQueryHandler(IApplicationDbContext db, ICurrentSession session)
    : IRequestHandler<OpenItemsForAllocationQuery, IReadOnlyList<OpenItemForAllocationDto>> {
    public async Task<IReadOnlyList<OpenItemForAllocationDto>> Handle(OpenItemsForAllocationQuery request, CancellationToken ct) {
        TreasuryAccess.RequireAny(session, TreasuryPermissions.Read, TreasuryPermissions.Collection, TreasuryPermissions.Payment);
        if (!await db.CurrentAccounts.AsNoTracking().AnyAsync(a => a.Id == request.CurrentAccountId, ct))
            throw new NotFoundException(request.CurrentAccountId.ToString(), "CurrentAccount");

        var lines = await (from l in db.LedgerDueLines.AsNoTracking()
                           join e in db.LedgerEntries on l.EntryId equals e.Id
                           where l.CurrentAccountId == request.CurrentAccountId && e.CancelledAt == null
                           select new { Line = l, e.Source, e.SourceId, e.DocumentNo, e.Description, e.Date, e.Created }).ToListAsync(ct);
        var allocations = await db.LedgerAllocations.AsNoTracking().Where(a => a.CurrentAccountId == request.CurrentAccountId).ToListAsync(ct);
        var active      = lines.Select(l => l.Line.Id).ToHashSet();

        var all    = new Dictionary<Guid, decimal>();
        var manual = new Dictionary<Guid, decimal>();
        foreach (var a in allocations) {
            if (!active.Contains(a.DebitLineId) || !active.Contains(a.CreditLineId)) continue;

            foreach (var id in new[] { a.DebitLineId, a.CreditLineId }) {
                all[id] = all.GetValueOrDefault(id) + a.Amount;
                if (a.Kind == AllocationKind.Manual) manual[id] = manual.GetValueOrDefault(id) + a.Amount;
            }
        }

        return lines
            .Select(l => new { Row = l, Open = l.Line.Amount - all.GetValueOrDefault(l.Line.Id), Allocatable = l.Line.Amount - manual.GetValueOrDefault(l.Line.Id) })
            .Where(x => x.Open > 0m || x.Allocatable > 0m)
            .OrderBy(x => x.Row.Line.Direction).ThenBy(x => x.Row.Line.DueDate).ThenBy(x => x.Row.Date).ThenBy(x => x.Row.Created).ThenBy(x => x.Row.Line.Sequence)
            .Select(x => new OpenItemForAllocationDto(x.Row.Line.Id, x.Row.Line.EntryId, x.Row.Source, x.Row.SourceId, x.Row.DocumentNo, x.Row.Description, x.Row.Date,
                                                      x.Row.Line.DueDate, x.Row.Line.Sequence, x.Row.Line.Direction, x.Row.Line.Amount, x.Open, x.Allocatable))
            .ToList();
    }
}
