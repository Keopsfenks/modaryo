using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Application.Inventory.Documents;
using ERP.Application.Settings;
using ERP.Application.Treasury.Accounts;
using ERP.Application.Treasury.ExpenseCategories;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Vouchers;

public enum ExpenseKind { Expense, Income }

public sealed record ExpenseResult(Guid Id, string Number, decimal NetAmount, decimal VatAmount, IReadOnlyList<string> Warnings);

/// <summary>
///     Masraf (MF) ya da gelir (GF) fişi. Tutar KDV dahildir; net ve KDV ayrılır (KDV muhasebe aktarımında gider için vatInput, gelir için vatOutput hesabına gider).
///     Tam biri verilir: hesap (hesabın para biriminde hareket) ya da cari (masraf cariyi alacaklandırır, gelir borçlandırır; tutar cari para birimindedir).
/// </summary>
[Authorize(Permission = TreasuryPermissions.Expense)]
public sealed record CreateExpenseCommand : IRequest<ExpenseResult> {
    public ExpenseKind Kind { get; init; }

    public Guid CategoryId { get; init; }

    public DateOnly Date { get; init; }

    public decimal Amount { get; init; }

    public decimal? VatRate { get; init; }

    public Guid? AccountId { get; init; }

    public Guid? CurrentAccountId { get; init; }

    public string? Description { get; init; }

    public Guid? AttachmentFileId { get; init; }
}

public sealed class CreateExpenseCommandValidator : AbstractValidator<CreateExpenseCommand> {
    public CreateExpenseCommandValidator() {
        RuleFor(c => c.Kind).IsInEnum().WithMessage("Fiş türü geçersiz.");
        RuleFor(c => c.CategoryId).NotEmpty().WithMessage("Kategori seçilmelidir.");
        RuleFor(c => c.Date).Must(d => d != default && CardRules.IsValidDate(d)).WithMessage(CardRules.DateMessage);
        RuleFor(c => c.Amount).GreaterThan(0m).LessThanOrEqualTo(CardRules.MaxAmount)
            .WithMessage("Tutar 0 değerinden büyük ve 999.999.999.999,99 değerinden küçük olmalıdır.");
        RuleFor(c => c.Amount).Must(CardRules.HasValidScale).WithMessage("Tutar en fazla 2 ondalık basamak içerebilir.");
        RuleFor(c => c.VatRate).InclusiveBetween(0m, 100m).When(c => c.VatRate is not null).WithMessage("KDV oranı 0 ile 100 arasında olmalıdır.")
            .Must(r => r is null || Math.Round(r.Value, 2) == r).WithMessage("KDV oranı en fazla 2 ondalık basamak içerebilir.");
        RuleFor(c => c.Description).MaximumLength(500).WithMessage("Açıklama en çok 500 karakter olabilir.");
    }
}

public sealed class CreateExpenseCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IPeriodLock           periodLock,
    ITenantSettings       settings,
    AutoAllocator         auto,
    IOutbox               outbox
) : IRequestHandler<CreateExpenseCommand, ExpenseResult> {
    public async Task<ExpenseResult> Handle(CreateExpenseCommand request, CancellationToken ct) {
        if ((request.AccountId is null) == (request.CurrentAccountId is null)) throw TreasuryErrors.ExpenseTarget();

        var expense = request.Kind == ExpenseKind.Expense;

        await using var tx = await db.BeginTransactionAsync(ct);
        if (request.CurrentAccountId is { } lockId && !await db.LockCurrentAccountAsync(lockId, ct))
            throw new NotFoundException(lockId.ToString(), "CurrentAccount");

        await periodLock.EnsureOpenAsync(request.Date, ct);
        await CategoryRules.EnsureSeededAsync(db, settings, ct);

        var category = await db.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
                       ?? throw new NotFoundException(request.CategoryId.ToString(), "ExpenseCategory");
        if (!category.IsActive) throw TreasuryErrors.CategoryKindMismatch("Pasif kategori kullanılamaz.");
        if (category.IsIncome == expense)
            throw TreasuryErrors.CategoryKindMismatch(expense ? "Masraf fişinde gelir kategorisi kullanılamaz." : "Gelir fişinde masraf kategorisi kullanılamaz.");

        TreasuryAccount? account = null;
        CurrentAccount?  cari    = null;
        string           currency;
        if (request.AccountId is { } accountId) {
            account = await AccountRules.FindAsync(db, tenant, accountId, ct);
            if (!account.IsActive) throw TreasuryErrors.AccountInactive();

            currency = account.Currency;
        } else {
            cari = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CurrentAccountId, ct)
                   ?? throw new NotFoundException(request.CurrentAccountId.ToString()!, "CurrentAccount");
            if (!cari.IsActive) throw CariErrors.AccountInactive();

            currency = cari.Currency;
        }

        var vatRate = request.VatRate is > 0m ? request.VatRate : null;
        var vat     = vatRate is { } rate ? TreasuryPosting.Round(request.Amount * rate / (100m + rate)) : 0m;
        var net     = request.Amount - vat;

        var number      = await DocumentNumbers.NextAsync(db, expense ? "MF" : "GF", request.Date, ct);
        var description = CardRules.Clean(request.Description);
        var document = new FinanceDocument {
                                               Kind              = expense ? FinanceDocumentKind.Expense : FinanceDocumentKind.Income,
                                               Number            = number,
                                               Date              = request.Date,
                                               AccountId         = account?.Id,
                                               CurrentAccountId  = cari?.Id,
                                               ExpenseCategoryId = category.Id,
                                               Amount            = request.Amount,
                                               Currency          = currency,
                                               VatRate           = vatRate,
                                               AttachmentFileId  = request.AttachmentFileId,
                                               Description       = description
                                           };
        db.FinanceDocuments.Add(document);

        if (account is not null) TreasuryPosting.Movement(db, account, document.Id, request.Date, expense ? -request.Amount : request.Amount, description);

        if (cari is not null) {
            var title = expense ? "Masraf" : "Gelir";
            TreasuryPosting.CariEntry(db, cari, !expense, request.Amount, request.Date, LedgerEntrySource.Payment, document.Id, number,
                                      description is null ? $"{title}: {category.Name}" : $"{title}: {description}");
        }

        await db.SaveChangesAsync(ct);
        if (cari is not null) {
            await auto.RebuildAsync(cari.Id, ct);
            await db.SaveChangesAsync(ct);
        }

        var warnings = new List<string>();
        if (expense && account is { Kind: TreasuryAccountKind.Cash } && await AccountRules.BalanceAsync(db, account.Id, ct) < 0m) warnings.Add("cash_negative");

        outbox.Enqueue("expense.recorded", new {
                                                   documentId = document.Id,
                                                   kind       = document.Kind.ToString(),
                                                   number,
                                                   categoryId = category.Id,
                                                   amount     = request.Amount,
                                                   vatRate,
                                                   currency
                                               });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ExpenseResult(document.Id, number, net, vat, warnings);
    }
}
