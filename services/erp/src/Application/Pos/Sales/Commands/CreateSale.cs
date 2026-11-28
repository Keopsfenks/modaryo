using ERP.Application.Common.Security;

namespace ERP.Application.Pos.Sales.Commands;

[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Sell)]
public sealed record CreateSaleCommand(SaleInput Input) : IRequest<SaleResultDto>;

public sealed class CreateSaleCommandValidator : AbstractValidator<CreateSaleCommand> {
    public CreateSaleCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.ClientSaleId).NotEmpty().WithMessage("İstemci satış kimliği (clientSaleId) zorunludur.");
        RuleFor(c => c.Input.RegisterId).NotEmpty().WithMessage("Kasa seçilmelidir.");
        RuleFor(c => c.Input.Lines).NotEmpty().WithMessage("Satışta en az bir satır olmalıdır.");
        RuleFor(c => c.Input.Payments).NotNull().WithMessage("Ödemeler gönderilmelidir.");
        RuleForEach(c => c.Input.Lines).ChildRules(l => {
            l.RuleFor(x => x.ProductId).NotEmpty().WithMessage("Kalem seçilmelidir.");
            l.RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Miktar sıfırdan büyük olmalıdır.");
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0m).When(x => x.UnitPrice is not null).WithMessage("Birim fiyat negatif olamaz.");
            l.RuleFor(x => x.ManualDiscountPercent).InclusiveBetween(0m, 100m).When(x => x.ManualDiscountPercent is not null).WithMessage("İskonto yüzdesi 0 ile 100 arasında olmalıdır.");
            l.RuleFor(x => x.FixedTotal).GreaterThan(0m).When(x => x.FixedTotal is not null).WithMessage("Terazi tutarı sıfırdan büyük olmalıdır.");
        });
        RuleForEach(c => c.Input.Payments).ChildRules(p => {
            p.RuleFor(x => x.Method).IsInEnum().WithMessage("Ödeme yöntemi geçersiz.");
            p.RuleFor(x => x.Amount).GreaterThan(0m).WithMessage("Ödeme tutarı sıfırdan büyük olmalıdır.");
        });
    }
}

public sealed class CreateSaleCommandHandler(PosSalePoster poster) : IRequestHandler<CreateSaleCommand, SaleResultDto> {
    public Task<SaleResultDto> Handle(CreateSaleCommand request, CancellationToken ct) {
        return poster.CreateAsync(request.Input, ct);
    }
}
