using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Instruments.Queries;

public sealed record InstrumentDto(
    Guid                Id,
    InstrumentKind      Kind,
    InstrumentDirection Direction,
    InstrumentStatus    Status,
    string              Number,
    DateOnly            DueDate,
    decimal             Amount,
    string              Currency,
    string              DrawerName,
    string?             BankName,
    string?             BankBranch,
    string?             AccountNo,
    int                 EndorsementCount,
    Guid                CurrentAccountId,
    string              CurrentAccountName,
    Guid?               EndorsedToId,
    string?             EndorsedToName,
    Guid?               CollectionAccountId,
    bool                ProtestFlag
);

public sealed record InstrumentEventDto(Guid Id, InstrumentStatus? From, InstrumentStatus To, DateOnly Date, string? Note, Guid? DocumentId, string? DocumentNumber, DateTimeOffset Created);

public sealed record InstrumentDetailDto(InstrumentDto Instrument, IReadOnlyList<InstrumentEventDto> History);

public static class InstrumentQueries {
    public static IQueryable<Instrument> Filter(IQueryable<Instrument> query, InstrumentDirection? direction, InstrumentStatus? status, DateOnly? dueFrom, DateOnly? dueTo, Guid? cariId) {
        if (direction is { } d) query = query.Where(i => i.Direction == d);
        if (status is { } s) query = query.Where(i => i.Status == s);
        if (dueFrom is { } from) query = query.Where(i => i.DueDate >= from);
        if (dueTo is { } to) query = query.Where(i => i.DueDate <= to);
        if (cariId is { } c) query = query.Where(i => i.CurrentAccountId == c || i.EndorsedToId == c);
        return query;
    }

    public static async Task<List<InstrumentDto>> ToDtosAsync(IApplicationDbContext db, List<Instrument> instruments, CancellationToken ct) {
        var ids   = instruments.Select(i => i.CurrentAccountId).Concat(instruments.Where(i => i.EndorsedToId != null).Select(i => i.EndorsedToId!.Value)).Distinct().ToList();
        var names = await db.CurrentAccounts.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return instruments.Select(i => new InstrumentDto(i.Id, i.Kind, i.Direction, i.Status, i.Number, i.DueDate, i.Amount, i.Currency, i.DrawerName, i.BankName, i.BankBranch, i.AccountNo,
                                                         i.EndorsementCount, i.CurrentAccountId, names.GetValueOrDefault(i.CurrentAccountId, string.Empty), i.EndorsedToId,
                                                         i.EndorsedToId is { } e ? names.GetValueOrDefault(e) : null, i.CollectionAccountId, i.ProtestFlag)).ToList();
    }
}

[Authorize(Permission = TreasuryPermissions.Read)]
[RequireModule("cek-senet")]
public sealed record ListInstrumentsQuery(InstrumentDirection? Direction, InstrumentStatus? Status, DateOnly? DueFrom, DateOnly? DueTo, Guid? CurrentAccountId)
    : IRequest<IReadOnlyList<InstrumentDto>>;

public sealed class ListInstrumentsQueryHandler(IApplicationDbContext db) : IRequestHandler<ListInstrumentsQuery, IReadOnlyList<InstrumentDto>> {
    public async Task<IReadOnlyList<InstrumentDto>> Handle(ListInstrumentsQuery request, CancellationToken ct) {
        var query = InstrumentQueries.Filter(db.Instruments.AsNoTracking(), request.Direction, request.Status, request.DueFrom, request.DueTo, request.CurrentAccountId);
        var items = await query.OrderBy(i => i.DueDate).ThenBy(i => i.Number).Take(1000).ToListAsync(ct);
        return await InstrumentQueries.ToDtosAsync(db, items, ct);
    }
}

[Authorize(Permission = TreasuryPermissions.Read)]
[RequireModule("cek-senet")]
public sealed record GetInstrumentQuery(Guid Id) : IRequest<InstrumentDetailDto>;

public sealed class GetInstrumentQueryHandler(IApplicationDbContext db) : IRequestHandler<GetInstrumentQuery, InstrumentDetailDto> {
    public async Task<InstrumentDetailDto> Handle(GetInstrumentQuery request, CancellationToken ct) {
        var instrument = await db.Instruments.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.Id, ct) ?? throw new NotFoundException(request.Id.ToString(), "Instrument");
        var dto        = (await InstrumentQueries.ToDtosAsync(db, [instrument], ct)).Single();

        var events  = await db.InstrumentEvents.AsNoTracking().Where(e => e.InstrumentId == instrument.Id).OrderBy(e => e.Date).ThenBy(e => e.Created).ToListAsync(ct);
        var docIds  = events.Where(e => e.DocumentId != null).Select(e => e.DocumentId!.Value).ToList();
        var numbers = await db.FinanceDocuments.AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
        var history = events.Select(e => new InstrumentEventDto(e.Id, e.From, e.To, e.Date, e.Note, e.DocumentId, e.DocumentId is { } id ? numbers.GetValueOrDefault(id) : null, e.Created)).ToList();
        return new InstrumentDetailDto(dto, history);
    }
}
