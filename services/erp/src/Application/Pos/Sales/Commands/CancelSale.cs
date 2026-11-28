using ERP.Application.Common.Security;
using ERP.Application.Pos.Sales.Queries;

namespace ERP.Application.Pos.Sales.Commands;

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Return)]
public sealed record CancelSaleCommand(Guid Id, string Reason) : IRequest<SaleDto>;

public sealed class CancelSaleCommandValidator : AbstractValidator<CancelSaleCommand> {
    public CancelSaleCommandValidator() {
        RuleFor(c => c.Reason).Must(r => r is not null && r.Trim().Length is >= 2 and <= 200).WithMessage("İptal nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

public sealed class CancelSaleCommandHandler(PosSalePoster poster, ISender sender) : IRequestHandler<CancelSaleCommand, SaleDto> {
    public async Task<SaleDto> Handle(CancelSaleCommand request, CancellationToken ct) {
        await poster.CancelAsync(request.Id, request.Reason.Trim(), ct);
        return await sender.Send(new GetSaleQuery(request.Id), ct);
    }
}
