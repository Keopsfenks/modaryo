using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Commands;

public sealed record AddEntryResult(
    StatementRow Entry,
    decimal      Balance,
    bool         CreditLimitExceeded
);

/// <summary>type: "opening" | "debit" | "credit". Açılışta direction ("debit" | "credit") zorunludur.</summary>
[Authorize(Permission = CariPermissions.Movements)]
public sealed record AddEntryCommand : IRequest<AddEntryResult> {
    public Guid AccountId { get; init; }

    public string Type { get; init; } = string.Empty;

    public DateOnly Date { get; init; }

    public decimal Amount { get; init; }

    public string? DocumentNo { get; init; }

    public string? Description { get; init; }

    public DateOnly? DueDate { get; init; }

    public string? Direction { get; init; }
}

public sealed class AddEntryCommandValidator : AbstractValidator<AddEntryCommand> {
    public AddEntryCommandValidator() {
        RuleFor(c => c.Type).Must(t => t is "opening" or "debit" or "credit")
            .WithMessage("Hareket türü açılış (opening), borç (debit) veya alacak (credit) olmalıdır.");
        RuleFor(c => c.Date).NotEqual(default(DateOnly)).WithMessage("Hareket tarihi zorunludur.");
        RuleFor(c => c.Date).Must(CardRules.IsValidDate).When(c => c.Date != default)
            .WithMessage(CardRules.DateMessage);
        RuleFor(c => c.Amount).GreaterThan(0m).LessThanOrEqualTo(CardRules.MaxAmount)
            .WithMessage("Tutar 0 değerinden büyük ve 999.999.999.999,99 değerinden küçük olmalıdır.");
        RuleFor(c => c.Amount).Must(CardRules.HasValidScale)
            .WithMessage("Tutar en fazla 2 ondalık basamak içerebilir.");
        RuleFor(c => c.Direction).Must(d => d is "debit" or "credit")
            .When(c => c.Type == "opening")
            .WithMessage("Açılış yönü borç (debit) veya alacak (credit) olmalıdır.");
        RuleFor(c => c.DocumentNo).MaximumLength(50).WithMessage("Belge no en çok 50 karakter olabilir.");
        RuleFor(c => c.Description).MaximumLength(500).WithMessage("Açıklama en çok 500 karakter olabilir.");
    }
}

public sealed class AddEntryCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<AddEntryCommand, AddEntryResult> {
    public async Task<AddEntryResult> Handle(AddEntryCommand request, CancellationToken ct) {
        var account = await db.CurrentAccounts.FirstOrDefaultAsync(a => a.Id == request.AccountId, ct)
                      ?? throw new NotFoundException(request.AccountId.ToString(), "CurrentAccount");
        if (!account.IsActive) throw CariErrors.AccountInactive();

        var type = request.Type switch {
                       "opening" => LedgerEntryType.Opening,
                       "debit"   => LedgerEntryType.DebitNote,
                       _         => LedgerEntryType.CreditNote
                   };

        if (type == LedgerEntryType.Opening
            && await db.LedgerEntries.AnyAsync(e => e.CurrentAccountId == account.Id &&
                                                    e.Type             == LedgerEntryType.Opening
                                                    && e.CancelledAt   == null, ct))
            throw CariErrors.OpeningExists();

        var debit = type switch {
                        LedgerEntryType.Opening   => request.Direction == "debit",
                        LedgerEntryType.DebitNote => true,
                        _                         => false
                    };
        var dueDate = request.DueDate ??
                      (type == LedgerEntryType.DebitNote ? request.Date.AddDays(account.DefaultDueDays) : null);

        var entry = EntryRules.NewEntry(account, type, request.Date, dueDate, request.DocumentNo, request.Description,
                                        request.Amount, debit);
        db.LedgerEntries.Add(entry);

        try {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (CariErrors.FromUniqueViolation(ex) is { } conflict) {
            throw conflict;
        }

        var balance = await db.LedgerEntries.Where(e => e.CurrentAccountId == account.Id && e.CancelledAt == null)
                          .SumAsync(e => e.Debit - e.Credit, ct);
        var exceeded = entry.Debit > 0 && account.CreditLimit is { } limit && balance > limit;

        return new AddEntryResult(EntryRules.ToRow(entry, balance), balance, exceeded);
    }
}