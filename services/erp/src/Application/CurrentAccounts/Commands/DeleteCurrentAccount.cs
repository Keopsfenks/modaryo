using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.CurrentAccounts.Commands;

[Authorize(Permission = CariPermissions.Manage)]
public sealed record DeleteCurrentAccountCommand(Guid Id) : IRequest;

public sealed class DeleteCurrentAccountCommandHandler(IApplicationDbContext db) : IRequestHandler<DeleteCurrentAccountCommand>
{
    public async Task Handle(DeleteCurrentAccountCommand request, CancellationToken ct)
    {
        var account = await db.CurrentAccounts.Include(a => a.Addresses).FirstOrDefaultAsync(a => a.Id == request.Id, ct)
            ?? throw new NotFoundException(request.Id.ToString(), "CurrentAccount");

        // İptal edilmiş hareketler de sayılır.
        if (await db.LedgerEntries.AnyAsync(e => e.CurrentAccountId == account.Id, ct))
        {
            throw CariErrors.HasMovements();
        }

        db.CurrentAccountAddresses.RemoveRange(account.Addresses);
        db.CurrentAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
    }
}
