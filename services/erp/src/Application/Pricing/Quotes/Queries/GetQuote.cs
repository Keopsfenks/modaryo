using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Domain.Pricing;

namespace ERP.Application.Pricing.Quotes.Queries;

[Authorize(Permission = PricingPermissions.Read)]
public sealed record GetQuoteQuery(
    PriceListKind Kind,
    Guid?         CurrentAccountId,
    Guid          ProductId,
    Guid?         VariantId,
    string?       Unit,
    decimal       Quantity,
    DateOnly?     Date
) : IRequest<PriceQuote>;

public sealed class GetQuoteQueryValidator : AbstractValidator<GetQuoteQuery> {
    public GetQuoteQueryValidator() {
        RuleFor(q => q.Unit).NotEmpty().WithMessage("Birim zorunludur.");
        RuleFor(q => q.Quantity).GreaterThan(0m).WithMessage("Miktar sıfırdan büyük olmalıdır.");
    }
}

public sealed class GetQuoteQueryHandler(
    IPriceQuoteService quotes,
    ICurrentSession    session,
    TimeProvider       time
) : IRequestHandler<GetQuoteQuery, PriceQuote> {
    public async Task<PriceQuote> Handle(GetQuoteQuery request, CancellationToken ct) {
        PricingRules.EnsureKindAccess(session, request.Kind);

        var date = request.Date ?? CurrentAccountClock.Today(time);
        var quote = await quotes.QuoteAsync(new QuoteRequest(request.Kind, request.CurrentAccountId, request.ProductId,
                                                             request.VariantId, request.Unit!.Trim(), request.Quantity, date), ct);
        return quote ?? throw new AppException(404, "price_not_found", "Bu kalem için geçerli bir fiyat bulunamadı.");
    }
}
