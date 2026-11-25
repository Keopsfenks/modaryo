using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.Treasury.ExchangeDifferences.Queries;

[Authorize(Permission = TreasuryPermissions.Read)]
public sealed record ListExchangeDifferencesQuery(Guid? CurrentAccountId, DateOnly? From, DateOnly? To) : IRequest<IReadOnlyList<ExchangeDifferenceDto>>;

public sealed class ListExchangeDifferencesQueryHandler(ExchangeDifferenceReader reader) : IRequestHandler<ListExchangeDifferencesQuery, IReadOnlyList<ExchangeDifferenceDto>> {
    public async Task<IReadOnlyList<ExchangeDifferenceDto>> Handle(ListExchangeDifferencesQuery request, CancellationToken ct) {
        return await reader.ListAsync(request.CurrentAccountId, request.From, request.To, ct);
    }
}
