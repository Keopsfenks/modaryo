using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Instruments.Queries;

public sealed record PortfolioRow(InstrumentDirection Direction, InstrumentStatus Status, string Currency, int Count, decimal Total);

/// <summary>Portföy: yön, durum ve para birimine göre adet ve toplam.</summary>
[Authorize(Permission = TreasuryPermissions.Read)]
[RequireModule("cek-senet")]
public sealed record PortfolioQuery : IRequest<IReadOnlyList<PortfolioRow>>;

public sealed class PortfolioQueryHandler(IApplicationDbContext db) : IRequestHandler<PortfolioQuery, IReadOnlyList<PortfolioRow>> {
    public async Task<IReadOnlyList<PortfolioRow>> Handle(PortfolioQuery request, CancellationToken ct) {
        var rows = await db.Instruments.AsNoTracking().GroupBy(i => new { i.Direction, i.Status, i.Currency })
                       .Select(g => new { g.Key.Direction, g.Key.Status, g.Key.Currency, Count = g.Count(), Total = g.Sum(i => i.Amount) }).ToListAsync(ct);
        return rows.OrderBy(r => r.Direction).ThenBy(r => r.Status).ThenBy(r => r.Currency)
            .Select(r => new PortfolioRow(r.Direction, r.Status, r.Currency, r.Count, r.Total)).ToList();
    }
}
