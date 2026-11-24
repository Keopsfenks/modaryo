using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Settings;
using ERP.Application.Treasury.Accounts;
using ERP.Application.Treasury.ExpenseCategories;
using ERP.Application.Treasury.Vouchers;

namespace ERP.Application.Treasury.ExchangeDifferences.Commands;

public sealed record DifferenceExpenseResult(Guid ExpenseId, string Number, decimal Amount);

/// <summary>
///     Seçilen negatif kur farklarından kur farkı gideri (kategori kur-farki) fişi yazar. Hesap verilmezse fiş cariyi alacaklandırır
///     (artakalan borcu kapatır); verilirse TL kasa/banka hesabından çıkış yapar. Her fark bir kez işlenir.
/// </summary>
[Authorize(Permission = TreasuryPermissions.Expense)]
public sealed record CreateDifferenceExpenseCommand(IReadOnlyList<Guid> AllocationIds, Guid? AccountId) : IRequest<DifferenceExpenseResult>;

public sealed class CreateDifferenceExpenseCommandValidator : AbstractValidator<CreateDifferenceExpenseCommand> {
    public CreateDifferenceExpenseCommandValidator() {
        RuleFor(c => c.AllocationIds).NotEmpty().WithMessage("En az bir kur farkı seçilmelidir.")
            .Must(l => l.Count <= 500).WithMessage("En fazla 500 tahsis seçilebilir.");
    }
}

public sealed class CreateDifferenceExpenseCommandHandler(
    IApplicationDbContext    db,
    ExchangeDifferenceReader reader,
    ExpensePoster            poster,
    ITenantSettings          settings,
    ICurrentTenant           tenant,
    IOutbox                  outbox,
    TimeProvider             time
) : IRequestHandler<CreateDifferenceExpenseCommand, DifferenceExpenseResult> {
    public async Task<DifferenceExpenseResult> Handle(CreateDifferenceExpenseCommand request, CancellationToken ct) {
        var ids    = Settlement.Distinct(request.AllocationIds);
        var cariId = await Settlement.CariOfAsync(db, ids, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockCurrentAccountAsync(cariId, ct)) throw new NotFoundException(cariId.ToString(), "CurrentAccount");

        var (_, total) = await Settlement.LoadAsync(reader, cariId, ids, ct);
        if (total >= 0m) throw TreasuryErrors.DifferenceNotApplicable("Kur farkı gideri yalnızca negatif fark için girilir; pozitif fark için fatura kesin.");

        if (request.AccountId is { } accountId) {
            var account = await AccountRules.FindAsync(db, tenant, accountId, ct);
            if (account.Currency != "TRY") throw TreasuryErrors.AccountCurrencyMismatch("Kur farkı gideri yalnızca TL hesaptan çıkabilir.");
        }

        await CategoryRules.EnsureSeededAsync(db, settings, ct);
        var category = await db.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Code == "kur-farki", ct)
                       ?? throw new NotFoundException("kur-farki", "ExpenseCategory");
        var amount = -total;
        var result = await poster.PostAsync(new CreateExpenseCommand {
                                                                        Kind             = ExpenseKind.Expense,
                                                                        CategoryId       = category.Id,
                                                                        Date             = CurrentAccountClock.Today(time),
                                                                        Amount           = amount,
                                                                        AccountId        = request.AccountId,
                                                                        CurrentAccountId = request.AccountId is null ? cariId : null,
                                                                        Description      = "Kur farkı gideri"
                                                                    }, ct);
        Settlement.Record(db, outbox, cariId, total, "Expense", result.Id, ids);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new DifferenceExpenseResult(result.Id, result.Number, amount);
    }
}
