using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Allocations.Commands;

public sealed record AllocationResult(Guid Id, Guid DebitLineId, Guid CreditLineId, decimal Amount);

public sealed record AllocationInput(Guid DueLineId, decimal Amount);

/// <summary>
///     Kaynak satır: <c>creditLineId</c> (tahsilat/ödemenin vade satırı; tedarikçi ödemesinde bu borç tarafıdır, karşı satırlar alacak olur) ya da
///     <c>paymentId</c> (tahsilat/ödeme belgesi). İkisinden tam biri verilir.
/// </summary>
[Authorize]
public sealed record AllocateCommand : IRequest<IReadOnlyList<AllocationResult>> {
    public Guid? CreditLineId { get; init; }

    public Guid? PaymentId { get; init; }

    public List<AllocationInput> Allocations { get; init; } = [];
}

public sealed class AllocateCommandValidator : AbstractValidator<AllocateCommand> {
    public AllocateCommandValidator() {
        RuleFor(c => c).Must(c => (c.CreditLineId is null) != (c.PaymentId is null))
            .WithMessage("creditLineId ya da paymentId'den tam biri verilmelidir.");
        RuleFor(c => c.Allocations).NotEmpty().WithMessage("En az bir eşleştirme satırı gerekir.")
            .Must(a => a.Count <= 200).WithMessage("Bir istekte en fazla 200 satır eşleştirilebilir.");
        RuleForEach(c => c.Allocations).ChildRules(a => {
            a.RuleFor(x => x.DueLineId).NotEmpty().WithMessage("Vade satırı zorunludur.");
            a.RuleFor(x => x.Amount).GreaterThan(0m).LessThanOrEqualTo(CardRules.MaxAmount).WithMessage("Eşleştirme tutarı sıfırdan büyük olmalıdır.");
            a.RuleFor(x => x.Amount).Must(CardRules.HasValidScale).WithMessage("Eşleştirme tutarı en fazla 2 ondalık basamak içerebilir.");
        });
    }
}

public sealed class AllocateCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    ManualAllocator       allocator
) : IRequestHandler<AllocateCommand, IReadOnlyList<AllocationResult>> {
    public async Task<IReadOnlyList<AllocationResult>> Handle(AllocateCommand request, CancellationToken ct) {
        TreasuryAccess.RequireAny(session, TreasuryPermissions.Collection, TreasuryPermissions.Payment);

        // Kilit için yalnızca cari belirlenir (izlemesiz); doğrulamalar kilit alındıktan sonra yapılır.
        var (_, accountId) = await ResolveSourceAsync(request, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockCurrentAccountAsync(accountId, ct)) throw new NotFoundException(accountId.ToString(), "CurrentAccount");

        // Kilidi beklerken kaynak belge iptal edilmiş olabilir: kilit altında yeniden çözülür.
        var (lineId, _) = await ResolveSourceAsync(request, ct);
        var items = request.Allocations.Select(a => new AllocationItem(a.DueLineId, a.Amount)).ToList();
        await allocator.AllocateAsync(accountId, lineId, items, ct);

        var ids = items.Select(i => i.DueLineId).ToList();
        var created = await db.LedgerAllocations.AsNoTracking()
                          .Where(a => a.Kind == AllocationKind.Manual && (a.DebitLineId == lineId || a.CreditLineId == lineId) &&
                                      (ids.Contains(a.DebitLineId) || ids.Contains(a.CreditLineId)))
                          .OrderBy(a => a.CreatedAt).Select(a => new AllocationResult(a.Id, a.DebitLineId, a.CreditLineId, a.Amount)).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return created;
    }

    private async Task<(Guid LineId, Guid AccountId)> ResolveSourceAsync(AllocateCommand request, CancellationToken ct) {
        if (request.CreditLineId is { } lineId) {
            var line = await db.LedgerDueLines.AsNoTracking().Where(l => l.Id == lineId).Select(l => new { l.Id, l.CurrentAccountId }).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException(lineId.ToString(), "LedgerDueLine");
            return (line.Id, line.CurrentAccountId);
        }

        var paymentId = request.PaymentId!.Value;
        var document = await db.FinanceDocuments.AsNoTracking().Where(d => d.Id == paymentId).Select(d => new { d.Kind, d.CancelledAt, d.CurrentAccountId })
                           .FirstOrDefaultAsync(ct) ?? throw new NotFoundException(paymentId.ToString(), "FinanceDocument");
        if (document.Kind is not (FinanceDocumentKind.Collection or FinanceDocumentKind.Payment) || document.CurrentAccountId is not { } accountId)
            throw TreasuryErrors.AllocationInvalid("Yalnızca tahsilat ve ödeme belgeleri eşleştirilebilir.");

        if (document.CancelledAt is not null) throw TreasuryErrors.AllocationLineCancelled();

        var due = await (from e in db.LedgerEntries.AsNoTracking()
                         join l in db.LedgerDueLines on e.Id equals l.EntryId
                         where e.Source == LedgerEntrySource.Payment && e.SourceId == paymentId && e.CancelledAt == null
                         select l.Id).FirstOrDefaultAsync(ct);
        if (due == Guid.Empty) throw TreasuryErrors.AllocationLineCancelled();

        return (due, accountId);
    }
}
