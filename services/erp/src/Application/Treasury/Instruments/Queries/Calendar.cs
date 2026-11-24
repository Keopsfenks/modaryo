using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Instruments.Queries;

public sealed record CalendarDay(DateOnly DueDate, IReadOnlyList<InstrumentDto> Items);

/// <summary>Vade takvimi: açık (portföyde, tahsilde ya da verilmiş) çek/senetler vade gününe göre.</summary>
[Authorize(Permission = TreasuryPermissions.Read)]
[RequireModule("cek-senet")]
public sealed record CalendarQuery(DateOnly? From, DateOnly? To) : IRequest<IReadOnlyList<CalendarDay>>;

public sealed class CalendarQueryHandler(IApplicationDbContext db) : IRequestHandler<CalendarQuery, IReadOnlyList<CalendarDay>> {
    public static readonly InstrumentStatus[] OpenStatuses = [InstrumentStatus.InPortfolio, InstrumentStatus.InCollection, InstrumentStatus.Issued];

    public async Task<IReadOnlyList<CalendarDay>> Handle(CalendarQuery request, CancellationToken ct) {
        var query = db.Instruments.AsNoTracking().Where(i => OpenStatuses.Contains(i.Status));
        if (request.From is { } from) query = query.Where(i => i.DueDate >= from);
        if (request.To is { } to) query = query.Where(i => i.DueDate <= to);

        var items = await query.OrderBy(i => i.DueDate).ThenBy(i => i.Number).Take(2000).ToListAsync(ct);
        var dtos  = await InstrumentQueries.ToDtosAsync(db, items, ct);
        return dtos.GroupBy(d => d.DueDate).Select(g => new CalendarDay(g.Key, g.ToList())).ToList();
    }
}
