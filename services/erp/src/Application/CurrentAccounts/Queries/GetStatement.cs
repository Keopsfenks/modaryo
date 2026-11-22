using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.CurrentAccounts;
using FluentValidation.Results;
using ValidationException = ERP.Application.Common.Exceptions.ValidationException;

namespace ERP.Application.CurrentAccounts.Queries;

public sealed record StatementResultDto(
    decimal                     OpeningBalance,
    IReadOnlyList<StatementRow> Rows,
    decimal                     TotalDebit,
    decimal                     TotalCredit,
    decimal                     ClosingBalance,
    string                      Currency
);

[Authorize(Permission = CariPermissions.Read)]
public sealed record GetStatementQuery(
    Guid      AccountId,
    DateOnly? From,
    DateOnly? To
) : IRequest<StatementResultDto>;

public sealed class GetStatementQueryHandler(
    IApplicationDbContext db,
    TimeProvider          time
)
    : IRequestHandler<GetStatementQuery, StatementResultDto> {
    public async Task<StatementResultDto> Handle(GetStatementQuery request, CancellationToken ct) {
        var today = CurrentAccountClock.Today(time);
        var from  = request.From ?? new DateOnly(today.Year, 1, 1);
        var to    = request.To   ?? today;
        if (from > to)
            throw new ValidationException([
                                              new ValidationFailure("from",
                                                                    "Başlangıç tarihi bitiş tarihinden sonra olamaz.")
                                          ]);

        var currency = await db.CurrentAccounts.AsNoTracking().Where(a => a.Id == request.AccountId)
                           .Select(a => a.Currency).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException(request.AccountId.ToString(), "CurrentAccount");
        var entries = await db.LedgerEntries.AsNoTracking().Where(e => e.CurrentAccountId == request.AccountId)
                          .ToListAsync(ct);

        var s = Statement.Build(entries, from, to);
        return new StatementResultDto(s.OpeningBalance, s.Rows, s.TotalDebit, s.TotalCredit, s.ClosingBalance,
                                      currency);
    }
}