using ERP.Application.Common.Interfaces;
using ERP.Application.Invoicing.Posting;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.Treasury;

/// <summary>
///     Faturanın vade satırlarında elle eşleştirme (tahsilat/ödeme) varsa iptal engellenir. Çağıran (fatura iptali) carinin kilidini
///     almış olmalıdır: eşleştirme yazımları aynı kilidi aldığından denetim ile yazım birbirini görür. İptal edilmiş bir belgenin eşleştirmeleri
///     o belgenin iptalinde silindiğinden burada ayrıca elenmez.
/// </summary>
public sealed class InvoicePaymentGuard(IApplicationDbContext db) : IInvoicePaymentGuard {
    public async Task<bool> HasManualAllocationsAsync(Guid invoiceId, CancellationToken ct) {
        var entryId = await db.Invoices.AsNoTracking().Where(i => i.Id == invoiceId).Select(i => i.LedgerEntryId).FirstOrDefaultAsync(ct);
        if (entryId is not { } id) return false;

        var lineIds = db.LedgerDueLines.Where(l => l.EntryId == id).Select(l => l.Id);
        return await db.LedgerAllocations.AnyAsync(a => a.Kind == AllocationKind.Manual && (lineIds.Contains(a.DebitLineId) || lineIds.Contains(a.CreditLineId)), ct);
    }
}
