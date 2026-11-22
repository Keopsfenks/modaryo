using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Commands;

[Authorize(Permission = CariPermissions.Manage)]
public sealed record AddAddressCommand(
    Guid         AccountId,
    AddressInput Address
) : IRequest<AddressDto>;

public sealed class AddAddressCommandValidator : AbstractValidator<AddAddressCommand> {
    public AddAddressCommandValidator() {
        RuleFor(c => c.Address).NotNull().WithMessage("Adres bilgisi zorunludur.");
        RuleFor(c => c.Address).SetValidator(new AddressInputValidator()).When(c => c.Address is not null);
    }
}

public sealed class AddAddressCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<AddAddressCommand, AddressDto> {
    public async Task<AddressDto> Handle(AddAddressCommand request, CancellationToken ct) {
        var all     = await AddressRules.LoadAsync(db, request.AccountId, ct);
        var address = new CurrentAccountAddress { CurrentAccountId = request.AccountId };
        AddressRules.Apply(address, request.Address);
        all.Add(address);
        AddressRules.ResolveDefaults(all, address, request.Address.IsDefaultBilling, request.Address.IsDefaultShipping);
        db.CurrentAccountAddresses.Add(address);
        await db.SaveChangesAsync(ct);
        return CurrentAccountReader.ToDto(address);
    }
}