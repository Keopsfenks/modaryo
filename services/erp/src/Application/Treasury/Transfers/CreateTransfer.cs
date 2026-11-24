using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Inventory.Documents;
using ERP.Application.Settings;
using ERP.Application.Treasury.Accounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Transfers;

/// <summary>Hesaplar arası virman. Tutar çıkış hesabının para biriminde; para birimleri farklıysa kur (yabancı paranın TL karşılığı) zorunludur.</summary>
[Authorize(Permission = TreasuryPermissions.Transfer)]
public sealed record CreateTransferCommand : IRequest<PostResult> {
    public Guid FromAccountId { get; init; }

    public Guid ToAccountId { get; init; }

    public DateOnly Date { get; init; }

    public decimal Amount { get; init; }

    public decimal? Rate { get; init; }

    public string? Description { get; init; }
}

public sealed class CreateTransferCommandValidator : AbstractValidator<CreateTransferCommand> {
    public CreateTransferCommandValidator() {
        RuleFor(c => c.FromAccountId).NotEmpty().WithMessage("Çıkış hesabı seçilmelidir.");
        RuleFor(c => c.ToAccountId).NotEmpty().WithMessage("Giriş hesabı seçilmelidir.");
        RuleFor(c => c).Must(c => c.FromAccountId != c.ToAccountId).WithMessage("Çıkış ve giriş hesabı aynı olamaz.");
        RuleFor(c => c.Date).Must(d => d != default && CardRules.IsValidDate(d)).WithMessage(CardRules.DateMessage);
        RuleFor(c => c.Amount).GreaterThan(0m).LessThanOrEqualTo(CardRules.MaxAmount)
            .WithMessage("Tutar 0 değerinden büyük ve 999.999.999.999,99 değerinden küçük olmalıdır.");
        RuleFor(c => c.Amount).Must(CardRules.HasValidScale).WithMessage("Tutar en fazla 2 ondalık basamak içerebilir.");
        RuleFor(c => c.Rate).GreaterThan(0m).LessThan(1_000_000m).When(c => c.Rate is not null).WithMessage("Kur sıfırdan büyük olmalıdır.")
            .Must(r => r is null || Math.Round(r.Value, 6) == r).WithMessage("Kur en fazla 6 ondalık basamak içerebilir.");
        RuleFor(c => c.Description).MaximumLength(500).WithMessage("Açıklama en çok 500 karakter olabilir.");
    }
}

public sealed class CreateTransferCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IPeriodLock           periodLock,
    IOutbox               outbox
) : IRequestHandler<CreateTransferCommand, PostResult> {
    public async Task<PostResult> Handle(CreateTransferCommand request, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        await periodLock.EnsureOpenAsync(request.Date, ct);

        var from = await AccountRules.FindAsync(db, tenant, request.FromAccountId, ct);
        var to   = await AccountRules.FindAsync(db, tenant, request.ToAccountId, ct);
        if (!from.IsActive || !to.IsActive) throw TreasuryErrors.AccountInactive();

        var toAmount = request.Amount;
        decimal? rate = null;
        if (from.Currency != to.Currency) {
            if (from.Currency != "TRY" && to.Currency != "TRY") throw TreasuryErrors.TransferCurrencyPair();

            rate = request.Rate ?? throw TreasuryErrors.TransferRateRequired();
            // Kur yabancı paranın TL karşılığıdır: yabancıdan TL'ye çarpılır, TL'den yabancıya bölünür.
            toAmount = TreasuryPosting.Round(from.Currency == "TRY" ? request.Amount / rate.Value : request.Amount * rate.Value);
        }

        var number = await DocumentNumbers.NextAsync(db, "VR", request.Date, ct);
        var description = CardRules.Clean(request.Description);
        var document = new FinanceDocument {
                                               Kind            = FinanceDocumentKind.Transfer,
                                               Number          = number,
                                               Date            = request.Date,
                                               AccountId       = from.Id,
                                               TargetAccountId = to.Id,
                                               Amount          = request.Amount,
                                               Currency        = from.Currency,
                                               Rate            = rate,
                                               Description     = description
                                           };
        db.FinanceDocuments.Add(document);
        TreasuryPosting.Movement(db, from, document.Id, request.Date, -request.Amount, description);
        TreasuryPosting.Movement(db, to, document.Id, request.Date, toAmount, description);

        outbox.Enqueue("transfer.recorded", new {
                                                    documentId    = document.Id,
                                                    number,
                                                    fromAccountId = from.Id,
                                                    toAccountId   = to.Id,
                                                    amount        = request.Amount,
                                                    currency      = from.Currency
                                                });
        await db.SaveChangesAsync(ct);

        var warnings = new List<string>();
        if (from.Kind == TreasuryAccountKind.Cash && await AccountRules.BalanceAsync(db, from.Id, ct) < 0m) warnings.Add("cash_negative");

        await tx.CommitAsync(ct);
        return new PostResult(document.Id, number, warnings);
    }
}
