using ERP.Application.Common.Security;

namespace ERP.Application.Invoicing.Queries;

/// <summary>Kaydetmeden toplamları hesaplar (aynı doğrulama ve hesaplayıcı).</summary>
[Authorize(Permission = InvoicingPermissions.Read)]
public sealed record PreviewTotalsQuery(InvoiceInput Input) : IRequest<PreviewDto>;

public sealed class PreviewTotalsQueryValidator : AbstractValidator<PreviewTotalsQuery> {
    public PreviewTotalsQueryValidator() {
        RuleFor(q => q.Input).NotNull().SetValidator(new InvoiceInputValidator());
    }
}

public sealed class PreviewTotalsQueryHandler(
    InvoiceBuilder builder
) : IRequestHandler<PreviewTotalsQuery, PreviewDto> {
    public async Task<PreviewDto> Handle(PreviewTotalsQuery request, CancellationToken ct) {
        var built = await builder.BuildAsync(request.Input, null, true, ct);
        return new PreviewDto(built.Rate, built.RateDate, built.RateOverridden, built.Lines.Select(InvoiceRules.ToDto).ToList(), built.Totals, built.TotalsTl,
                              InvoiceRules.VatSummary(built.Lines), built.Cari.Currency, built.LedgerAmount);
    }
}
