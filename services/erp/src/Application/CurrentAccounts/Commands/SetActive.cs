using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.CurrentAccounts.Commands;

[Authorize(Permission = CariPermissions.Manage)]
public sealed record SetCurrentAccountActiveCommand(
    Guid Id,
    bool IsActive
) : IRequest;

public sealed class SetCurrentAccountActiveCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<SetCurrentAccountActiveCommand> {
    public async Task Handle(SetCurrentAccountActiveCommand request, CancellationToken ct) {
        var account = await db.CurrentAccounts.FirstOrDefaultAsync(a => a.Id == request.Id, ct)
                      ?? throw new NotFoundException(request.Id.ToString(), "CurrentAccount");
        account.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
    }
}