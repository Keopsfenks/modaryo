using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.Treasury.Allocations.Commands;

[Authorize]
public sealed record UnallocateCommand(Guid Id) : IRequest;

public sealed class UnallocateCommandHandler(
    IApplicationDbContext db,
    ICurrentSession       session,
    AutoAllocator         allocator
) : IRequestHandler<UnallocateCommand> {
    public async Task Handle(UnallocateCommand request, CancellationToken ct) {
        TreasuryAccess.RequireAny(session, TreasuryPermissions.Collection, TreasuryPermissions.Payment);

        var head = await db.LedgerAllocations.AsNoTracking().Where(a => a.Id == request.Id).Select(a => new { a.CurrentAccountId, a.Kind }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "LedgerAllocation");
        if (head.Kind != AllocationKind.Manual) throw TreasuryErrors.AllocationNotManual();

        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockCurrentAccountAsync(head.CurrentAccountId, ct)) throw new NotFoundException(head.CurrentAccountId.ToString(), "CurrentAccount");

        // Kilidi beklerken başka bir istek kaldırmış olabilir.
        var allocation = await db.LedgerAllocations.FirstOrDefaultAsync(a => a.Id == request.Id, ct)
                         ?? throw new NotFoundException(request.Id.ToString(), "LedgerAllocation");
        db.LedgerAllocations.Remove(allocation);
        await db.SaveChangesAsync(ct);

        await allocator.RebuildAsync(head.CurrentAccountId, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
