using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.Treasury.Journal.Commands;

/// <summary>Mahsup fişini iptal eder: cari satırları iptal edilir, eşleştirmeler yeniden kurulur. Dönem kilidi uygulanır.</summary>
[Authorize(Permission = TreasuryPermissions.Journal)]
public sealed record CancelVoucherCommand(Guid Id, string Reason) : IRequest;

public sealed class CancelVoucherCommandValidator : AbstractValidator<CancelVoucherCommand> {
    public CancelVoucherCommandValidator() {
        RuleFor(c => c.Reason).Must(r => r is not null && r.Trim().Length is >= 2 and <= 200).WithMessage("İptal nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

public sealed class CancelVoucherCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    IPeriodLock           periodLock,
    AutoAllocator         auto,
    IOutbox               outbox,
    TimeProvider          clock
) : IRequestHandler<CancelVoucherCommand> {
    public async Task Handle(CancelVoucherCommand request, CancellationToken ct) {
        var head = await db.JournalVouchers.AsNoTracking().Where(v => v.Id == request.Id).Select(v => v.Lines).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "JournalVoucher");
        var cariIds = head.Where(l => l.CurrentAccountId is not null).Select(l => l.CurrentAccountId!.Value).Distinct().OrderBy(id => id).ToList();

        await using var tx = await db.BeginTransactionAsync(ct);
        foreach (var id in cariIds)
            if (!await db.LockCurrentAccountAsync(id, ct))
                throw new NotFoundException(id.ToString(), "CurrentAccount");

        var voucher = await db.JournalVouchers.FirstOrDefaultAsync(v => v.Id == request.Id, ct)
                      ?? throw new NotFoundException(request.Id.ToString(), "JournalVoucher");
        if (voucher.IsCancelled) throw TreasuryErrors.VoucherCancelled();

        await periodLock.EnsureOpenAsync(voucher.Date, ct);

        var now    = clock.GetUtcNow();
        var reason = request.Reason.Trim();
        voucher.IsCancelled  = true;
        voucher.CancelledAt  = now;
        voucher.CancelReason = reason;

        foreach (var entry in await db.LedgerEntries.Where(e => e.Source == LedgerEntrySource.Voucher && e.SourceId == voucher.Id && e.CancelledAt == null).ToListAsync(ct)) {
            entry.CancelledAt  = now;
            entry.CancelledBy  = session.UserId?.ToString();
            entry.CancelReason = reason;
        }

        await db.SaveChangesAsync(ct);
        foreach (var id in cariIds) await auto.RebuildAsync(id, ct);

        outbox.Enqueue("journal_voucher.cancelled", new { voucherId = voucher.Id, number = voucher.Number, reason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
