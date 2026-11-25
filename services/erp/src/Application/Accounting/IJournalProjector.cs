using ERP.Domain.Treasury;

namespace ERP.Application.Accounting;

/// <summary>Yevmiye aktarımındaki tek fiş: bir kaynak belgenin (ya da iptalinin) muhasebe satırları. Tutarlar TL'dir.</summary>
public sealed record ProjectedVoucher(string VoucherNo, DateOnly Date, string DocumentType, string DocumentNo, IReadOnlyList<JournalLine> Lines);

/// <summary>Bir kaynak belge türünü [from, to] aralığında yevmiye fişlerine çevirir. İptal edilmiş belge: özgün fiş + iptal tarihli ters fiş (her biri kendi tarihiyle aralığa girer).</summary>
public interface IJournalProjector {
    string SourceType { get; }

    IAsyncEnumerable<ProjectedVoucher> ProjectAsync(DateOnly from, DateOnly to, Settings.Categories.AccountMapping map, CancellationToken ct);
}
