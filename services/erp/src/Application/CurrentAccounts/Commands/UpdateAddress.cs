using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.CurrentAccounts.Commands;

[Authorize(Permission = CariPermissions.Manage)]
public sealed record UpdateAddressCommand(
    Guid         AccountId,
    Guid         AddressId,
    AddressInput Address
) : IRequest<AddressDto>;

public sealed class UpdateAddressCommandValidator : AbstractValidator<UpdateAddressCommand> {
    public UpdateAddressCommandValidator() {
        RuleFor(c => c.Address).NotNull().WithMessage("Adres bilgisi zorunludur.");
        RuleFor(c => c.Address).SetValidator(new AddressInputValidator()).When(c => c.Address is not null);
    }
}

public sealed class UpdateAddressCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<UpdateAddressCommand, AddressDto> {
    public async Task<AddressDto> Handle(UpdateAddressCommand request, CancellationToken ct) {
        var all = await AddressRules.LoadAsync(db, request.AccountId, ct);
        var address = all.FirstOrDefault(a => a.Id == request.AddressId)
                      ?? throw new NotFoundException(request.AddressId.ToString(), "CurrentAccountAddress");
        AddressRules.Apply(address, request.Address);
        AddressRules.ResolveDefaults(all, address, request.Address.IsDefaultBilling, request.Address.IsDefaultShipping);
        await db.SaveChangesAsync(ct);
        return CurrentAccountReader.ToDto(address);
    }
}