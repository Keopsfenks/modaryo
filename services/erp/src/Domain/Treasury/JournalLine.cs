namespace ERP.Domain.Treasury;

public sealed record JournalLine(string AccountCode, decimal Debit, decimal Credit, string Description, Guid? CurrentAccountId);
