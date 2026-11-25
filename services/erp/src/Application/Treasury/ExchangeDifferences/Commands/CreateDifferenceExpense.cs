using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Settings;
using ERP.Application.Treasury.ExpenseCategories;
using ERP.Application.Treasury.Vouchers;

namespace ERP.Application.Treasury.ExchangeDifferences.Commands;

/// <summary>Kind: Expense (kur farkı gideri, cariyi alacaklandırır) ya da Income (kur farkı geliri, cariyi borçlandırır).</summary>
public sealed record DifferenceExpenseResult(Guid ExpenseId, string Number, decimal Amount, string Kind);

/// <summary>
///     Cari artığını kapatan kur farkı fişi. Negatif fark (cari borç artığı) kategori kur-farki gider fişi ile cariyi alacaklandırır; tedarikçi tarafındaki pozitif fark
///     (cari alacak artığı) kategori kur-farki-geliri gelir fişi ile cariyi borçlandırır. Fiş her zaman cariye yazılır: fark nakit hareketi değildir, bu nedenle
///     kasa/banka hesabı seçilemez (<see cref="AccountId" /> verilirse 400 difference_not_applicable). Müşteri tarafındaki pozitif fark için fatura kesilir.
///     Her fark bir kez işlenir; fiş iptal edilirse fark yeniden işlenebilir.
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
    IOutbox                  outbox,
    TimeProvider             time
) : IRequestHandler<CreateDifferenceExpenseCommand, DifferenceExpenseResult> {
    public async Task<DifferenceExpenseResult> Handle(CreateDifferenceExpenseCommand request, CancellationToken ct) {
        if (request.AccountId is not null)
            throw TreasuryErrors.DifferenceNotApplicable("Kur farkı nakit hareketi değildir; fiş her zaman cariye yazılır, kasa/banka hesabı seçilemez.");

        var ids    = Settlement.Distinct(request.AllocationIds);
        var cariId = await Settlement.CariOfAsync(db, ids, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockCurrentAccountAsync(cariId, ct)) throw new NotFoundException(cariId.ToString(), "CurrentAccount");

        var (rows, total, kind) = await Settlement.LoadAsync(reader, cariId, ids, ct);
        if (kind == DifferenceKind.Invoice) throw TreasuryErrors.DifferenceNotApplicable("Müşteri tarafındaki pozitif kur farkı için fatura kesin; gider/gelir fişi girilmez.");

        var expense = kind == DifferenceKind.Expense;
        await CategoryRules.EnsureSeededAsync(db, settings, ct);
        var code     = expense ? "kur-farki" : "kur-farki-geliri";
        var category = await db.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, ct)
                       ?? throw new NotFoundException(code, "ExpenseCategory");
        var amount = Math.Abs(total);
        var result = await poster.PostAsync(new CreateExpenseCommand {
                                                                        Kind             = expense ? ExpenseKind.Expense : ExpenseKind.Income,
                                                                        CategoryId       = category.Id,
                                                                        Date             = CurrentAccountClock.Today(time),
                                                                        Amount           = amount,
                                                                        CurrentAccountId = cariId,
                                                                        Description      = expense ? "Kur farkı gideri" : "Kur farkı geliri"
                                                                    }, ct);
        Settlement.Record(db, outbox, cariId, total, expense ? "Expense" : "Income", result.Id, ids, rows);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new DifferenceExpenseResult(result.Id, result.Number, amount, expense ? "Expense" : "Income");
    }
}
