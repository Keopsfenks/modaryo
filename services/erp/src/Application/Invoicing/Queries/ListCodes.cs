using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.Invoicing.Queries;

public sealed record WithholdingCodeDto(string Code, string Name, int Tenths);

public sealed record ExemptionCodeDto(string Code, string Name, string Group);

public sealed record InvoiceCodesDto(IReadOnlyList<WithholdingCodeDto> Withholding, IReadOnlyList<ExemptionCodeDto> Exemption);

/// <summary>Fatura satırlarında seçilebilen tevkifat ve istisna kodları (aktif olanlar, koda göre sıralı).</summary>
[Authorize(Permission = InvoicingPermissions.Read)]
public sealed record ListInvoiceCodesQuery : IRequest<InvoiceCodesDto>;

public sealed class ListInvoiceCodesQueryHandler(IApplicationDbContext db) : IRequestHandler<ListInvoiceCodesQuery, InvoiceCodesDto> {
    public async Task<InvoiceCodesDto> Handle(ListInvoiceCodesQuery request, CancellationToken ct) {
        var withholding = await db.WithholdingCodes.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code)
                                  .Select(c => new WithholdingCodeDto(c.Code, c.Name, c.Tenths)).ToListAsync(ct);
        var exemption = await db.ExemptionCodes.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code)
                                .Select(c => new ExemptionCodeDto(c.Code, c.Name, c.Group)).ToListAsync(ct);
        return new InvoiceCodesDto(withholding, exemption);
    }
}
