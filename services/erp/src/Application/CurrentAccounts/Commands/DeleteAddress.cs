using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.CurrentAccounts.Commands;

[Authorize(Permission = CariPermissions.Manage)]
public sealed record DeleteAddressCommand(
    Guid AccountId,
    Guid AddressId
) : IRequest;

public sealed class DeleteAddressCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<DeleteAddressCommand> {
    public async Task Handle(DeleteAddressCommand request, CancellationToken ct) {
        var all = await AddressRules.LoadAsync(db, request.AccountId, ct);
        var address = all.FirstOrDefault(a => a.Id == request.AddressId)
                      ?? throw new NotFoundException(request.AddressId.ToString(), "CurrentAccountAddress");
        all.Remove(address);
        db.CurrentAccountAddresses.Remove(address);
        AddressRules.EnsureDefaults(all);
        await db.SaveChangesAsync(ct);
    }
}