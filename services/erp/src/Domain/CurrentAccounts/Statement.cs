namespace ERP.Domain.CurrentAccounts;

public sealed record StatementRow(
    Guid            Id,
    DateOnly        Date,
    LedgerEntryType Type,
    string?         DocumentNo,
    string?         Description,
    decimal         Debit,
    decimal         Credit,
    DateOnly?       DueDate,
    decimal         RunningBalance,
    bool            Cancelled,
    string?         CancelReason,
    LedgerEntrySource Source   = LedgerEntrySource.Manual,
    Guid?           SourceId   = null
);

public sealed record StatementResult(
    decimal                     OpeningBalance,
    IReadOnlyList<StatementRow> Rows,
    decimal                     TotalDebit,
    decimal                     TotalCredit,
    decimal                     ClosingBalance
);

public static class Statement {
    /// <summary>Bakiye = Σ Borç − Σ Alacak (iptal edilmemiş hareketler).</summary>
    public static decimal Balance(IEnumerable<LedgerEntry> entries) {
        return entries.Where(e => !e.IsCancelled).Sum(e => e.Signed);
    }

    public static StatementResult Build(IEnumerable<LedgerEntry> entries, DateOnly from, DateOnly to) {
        var all     = entries.ToList();
        var opening = Balance(all.Where(e => e.Date < from));

        var     rows       = new List<StatementRow>();
        var     running    = opening;
        decimal totalDebit = 0, totalCredit = 0;

        foreach (var e in all.Where(e => e.Date >= from && e.Date <= to).OrderBy(e => e.Date).ThenBy(e => e.Created)) {
            if (!e.IsCancelled) {
                running     += e.Signed;
                totalDebit  += e.Debit;
                totalCredit += e.Credit;
            }

            rows.Add(new StatementRow(e.Id, e.Date, e.Type, e.DocumentNo, e.Description,
                                      e.Debit, e.Credit, e.DueDate, running, e.IsCancelled, e.CancelReason, e.Source, e.SourceId));
        }

        return new StatementResult(opening, rows, totalDebit, totalCredit, running);
    }
}