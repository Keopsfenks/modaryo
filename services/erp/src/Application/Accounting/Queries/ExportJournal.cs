using System.Globalization;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Application.Treasury;

namespace ERP.Application.Accounting.Queries;

/// <summary>Yevmiye defteri (.xlsx): her fiş satırı bir satır. Sütunlar: Fiş no, Tarih, Hesap kodu, Açıklama, Borç, Alacak, Belge türü, Belge no.</summary>
[Authorize(Permission = TreasuryPermissions.Export)]
public sealed record ExportJournalQuery(DateOnly From, DateOnly To) : IRequest<byte[]>;

public sealed class ExportJournalQueryValidator : AbstractValidator<ExportJournalQuery> {
    public ExportJournalQueryValidator() {
        RuleFor(q => q.From).Must(d => d != default).WithMessage("Başlangıç tarihi zorunludur.");
        RuleFor(q => q.To).Must(d => d != default).WithMessage("Bitiş tarihi zorunludur.");
        RuleFor(q => q).Must(q => q.From <= q.To).WithMessage("Başlangıç tarihi bitişten sonra olamaz.");
        RuleFor(q => q).Must(q => q.To.DayNumber - q.From.DayNumber <= 366).WithMessage("Aktarım aralığı en fazla 1 yıl olabilir.");
    }
}

public sealed class ExportJournalQueryHandler(
    ITenantSettings                 settings,
    IEnumerable<IJournalProjector>  projectors,
    IXlsxService                    xlsx
) : IRequestHandler<ExportJournalQuery, byte[]> {
    public static readonly IReadOnlyList<string> Headers = ["Fiş no", "Tarih", "Hesap kodu", "Açıklama", "Borç", "Alacak", "Belge türü", "Belge no"];

    public async Task<byte[]> Handle(ExportJournalQuery request, CancellationToken ct) {
        var map      = (await settings.GetAsync<FinanceSettings>(ct)).AccountMapping;
        var vouchers = new List<ProjectedVoucher>();
        foreach (var projector in projectors)
            await foreach (var voucher in projector.ProjectAsync(request.From, request.To, map, ct))
                vouchers.Add(voucher);

        // Her kaynak belge dengeli fiş üretmelidir; dengesiz fiş sessizce dışarı verilmez.
        var broken = vouchers.FirstOrDefault(v => !VoucherRules.IsBalanced(v));
        if (broken is not null) throw new InvalidOperationException($"{broken.DocumentType} {broken.DocumentNo} için fiş dengesiz.");

        var rows = vouchers.OrderBy(v => v.Date).ThenBy(v => v.VoucherNo, StringComparer.Ordinal)
                           .SelectMany(v => v.Lines.Select(l => (IReadOnlyList<object?>)new object?[] {
                               v.VoucherNo, v.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), l.AccountCode, l.Description, l.Debit, l.Credit, v.DocumentType, v.DocumentNo
                           }));
        return xlsx.Write("Yevmiye", Headers, rows);
    }
}
