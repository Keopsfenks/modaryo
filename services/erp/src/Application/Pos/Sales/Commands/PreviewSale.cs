using ERP.Application.Common.Security;

namespace ERP.Application.Pos.Sales.Commands;

/// <summary>Satış önizlemesi: gerçek satışla aynı hesap, yazma yok.</summary>
[RequireModule(PosPermissions.Module)]
[Authorize(Permission = PosPermissions.Sell)]
public sealed record PreviewSaleCommand(SalePreviewInput Input) : IRequest<SalePreviewDto>;

public sealed class PreviewSaleCommandValidator : AbstractValidator<PreviewSaleCommand> {
    public PreviewSaleCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.Lines).NotEmpty().WithMessage("Satışta en az bir satır olmalıdır.");
        RuleForEach(c => c.Input.Lines).ChildRules(l => {
            l.RuleFor(x => x.ProductId).NotEmpty().WithMessage("Kalem seçilmelidir.");
            l.RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Miktar sıfırdan büyük olmalıdır.");
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0m).When(x => x.UnitPrice is not null).WithMessage("Birim fiyat negatif olamaz.");
            l.RuleFor(x => x.ManualDiscountPercent).InclusiveBetween(0m, 100m).When(x => x.ManualDiscountPercent is not null).WithMessage("İskonto yüzdesi 0 ile 100 arasında olmalıdır.");
            l.RuleFor(x => x.FixedTotal).GreaterThan(0m).When(x => x.FixedTotal is not null).WithMessage("Terazi tutarı sıfırdan büyük olmalıdır.");
        });
    }
}

public sealed class PreviewSaleCommandHandler(PosSalePoster poster) : IRequestHandler<PreviewSaleCommand, SalePreviewDto> {
    public Task<SalePreviewDto> Handle(PreviewSaleCommand request, CancellationToken ct) {
        return poster.PreviewAsync(request.Input, ct);
    }
}
