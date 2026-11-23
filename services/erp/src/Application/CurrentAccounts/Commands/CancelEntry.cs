using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Commands;

[Authorize(Permission = CariPermissions.Movements)]
public sealed record CancelEntryCommand(
    Guid   AccountId,
    Guid   EntryId,
    string Reason
) : IRequest;

public sealed class CancelEntryCommandValidator : AbstractValidator<CancelEntryCommand> {
    public CancelEntryCommandValidator() {
        RuleFor(c => c.Reason).Must(r => r is not null && r.Trim().Length is >= 2 and <= 200)
            .WithMessage("İptal nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

public sealed class CancelEntryCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    TimeProvider          time,
    IPeriodLock           periodLock,
    AutoAllocator         allocator
)
    : IRequestHandler<CancelEntryCommand> {
    public async Task Handle(CancelEntryCommand request, CancellationToken ct) {
        await using var transaction = await db.BeginTransactionAsync(ct);
        if (!await db.LockCurrentAccountAsync(request.AccountId, ct))
            throw new NotFoundException(request.EntryId.ToString(), "LedgerEntry");

        var entry = await db.LedgerEntries.FirstOrDefaultAsync(e => e.Id               == request.EntryId &&
                                                                    e.CurrentAccountId == request.AccountId, ct)
                    ?? throw new NotFoundException(request.EntryId.ToString(), "LedgerEntry");

        if (entry.IsCancelled) throw CariErrors.AlreadyCancelled();

        if (entry.Source != LedgerEntrySource.Manual) throw CariErrors.NotManual();

        await periodLock.EnsureOpenAsync(entry.Date, ct);

        entry.CancelledAt  = time.GetUtcNow();
        entry.CancelledBy  = session.UserId?.ToString();
        entry.CancelReason = request.Reason.Trim();
        await db.SaveChangesAsync(ct);

        // İptal edilen satırın eşleştirmeleri sonraki satırlara kayar.
        await allocator.RebuildAsync(request.AccountId, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}