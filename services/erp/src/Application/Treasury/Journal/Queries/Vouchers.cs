using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Journal.Queries;

public sealed record VoucherLineDto(string AccountCode, decimal Debit, decimal Credit, string Description, Guid? CurrentAccountId);

public sealed record VoucherDto(
    Guid                          Id,
    string                        Number,
    DateOnly                      Date,
    string                        Description,
    string                        SourceType,
    Guid?                         SourceId,
    string?                       ExternalCaller,
    bool                          IsCancelled,
    string?                       CancelReason,
    decimal                       TotalDebit,
    decimal                       TotalCredit,
    IReadOnlyList<VoucherLineDto> Lines);

internal static class VoucherMap {
    public static VoucherDto ToDto(JournalVoucher v) {
        return new VoucherDto(v.Id, v.Number, v.Date, v.Description, v.SourceType, v.SourceId, v.ExternalCaller, v.IsCancelled, v.CancelReason, v.Lines.Sum(l => l.Debit),
                              v.Lines.Sum(l => l.Credit), v.Lines.Select(l => new VoucherLineDto(l.AccountCode, l.Debit, l.Credit, l.Description, l.CurrentAccountId)).ToList());
    }
}

[Authorize]
public sealed record ListVouchersQuery(DateOnly? From, DateOnly? To, string? SourceType, bool IncludeCancelled) : IRequest<IReadOnlyList<VoucherDto>>;

public sealed class ListVouchersQueryHandler(IApplicationDbContext db, ICurrentSession session) : IRequestHandler<ListVouchersQuery, IReadOnlyList<VoucherDto>> {
    public async Task<IReadOnlyList<VoucherDto>> Handle(ListVouchersQuery request, CancellationToken ct) {
        TreasuryAccess.RequireAny(session, TreasuryPermissions.Read, TreasuryPermissions.Journal, TreasuryPermissions.Export);

        var query = db.JournalVouchers.AsNoTracking();
        if (request.From is { } from) query = query.Where(v => v.Date >= from);
        if (request.To is { } to) query = query.Where(v => v.Date <= to);
        if (!string.IsNullOrWhiteSpace(request.SourceType)) query = query.Where(v => v.SourceType == request.SourceType);
        if (!request.IncludeCancelled) query = query.Where(v => !v.IsCancelled);

        var vouchers = await query.OrderByDescending(v => v.Date).ThenByDescending(v => v.Number).Take(500).ToListAsync(ct);
        return vouchers.Select(VoucherMap.ToDto).ToList();
    }
}

[Authorize]
public sealed record GetVoucherQuery(Guid Id) : IRequest<VoucherDto>;

public sealed class GetVoucherQueryHandler(IApplicationDbContext db, ICurrentSession session) : IRequestHandler<GetVoucherQuery, VoucherDto> {
    public async Task<VoucherDto> Handle(GetVoucherQuery request, CancellationToken ct) {
        TreasuryAccess.RequireAny(session, TreasuryPermissions.Read, TreasuryPermissions.Journal, TreasuryPermissions.Export);

        var voucher = await db.JournalVouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.Id, ct)
                      ?? throw new NotFoundException(request.Id.ToString(), "JournalVoucher");
        return VoucherMap.ToDto(voucher);
    }
}
