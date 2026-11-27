using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing.Posting;
using ERP.Application.Invoicing.Series;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing;

/// <summary>Önceden ayrılmış fatura numarası (hızlı fatura: seri satırı kilidi sevkin stok kilitlerinden önce alınır).</summary>
public sealed record ReservedNumber(DocumentSeries Series, long Sequence, InvoiceMode Mode, EInvoiceScenario? Scenario);

/// <summary>
///     Taslağı keser: numara, defter, vade ve stok kayıtları tek işlemde yazılır. İşlemi kendisi açar (ownTransaction) ya da
///     çağıranın açık işleminde çalışır (hızlı fatura); ikinci durumda onay çağırandadır.
///     Kilit sırası: cari, belge kilitleri (eklentiler; sipariş vb.), seri numarası satırı, stok.
/// </summary>
public sealed class InvoiceIssuer(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    IPeriodLock           periodLock,
    ITenantSettings       settings,
    InvoiceBuilder        builder,
    InvoicePoster         poster,
    SeriesResolver        seriesResolver,
    TimeProvider          clock
) {
    private async Task<(InvoiceMode Mode, EBelgeSettings Settings)> ModeAsync(CurrentAccount cari, CancellationToken ct) {
        var ebelge = await settings.GetAsync<EBelgeSettings>(ct);
        return (!ebelge.Enabled ? InvoiceMode.Paper : cari.IsEInvoiceTaxpayer ? InvoiceMode.EInvoice : InvoiceMode.EArchive, ebelge);
    }

    /// <summary>Seri satırını kilitleyerek bir sonraki numarayı ayırır (açık işlem gerekir).</summary>
    public async Task<ReservedNumber> ReserveNumberAsync(Guid branchId, InvoiceKind kind, DateOnly date, CurrentAccount cari, CancellationToken ct) {
        var (mode, ebelge) = await ModeAsync(cari, ct);
        var series   = await seriesResolver.ResolveAsync(InvoiceRules.GroupOf(kind), branchId, mode, date.Year, ct);
        // UPDATE ... RETURNING: satır kilidi işlem sonuna dek tutulur; aynı seride paralel kesimler sıraya girer, geri alınan numara geri gelir.
        var sequence = await db.ReserveSeriesNumberAsync(series.Id, ct) ?? throw InvoicingErrors.SeriesMissing();
        if (sequence > DocumentSeries.MaxSequence) throw InvoicingErrors.SeriesExhausted(series.Prefix);
        return new ReservedNumber(series, sequence, mode, mode == InvoiceMode.EInvoice ? ebelge.DefaultScenario : null);
    }

    public async Task<(Invoice Invoice, string CariName)> IssueAsync(Guid invoiceId, bool ownTransaction, CancellationToken ct, ReservedNumber? reserved = null) {
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.DueSchedule).FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new NotFoundException(invoiceId.ToString(), "Invoice");
        InvoiceRules.EnsureKindPermission(session, invoice.Kind);
        BranchGuard.Ensure(tenant, invoice.BranchId);
        if (invoice.Status != InvoiceStatus.Draft) throw InvoiceErrors.NotDraft(invoice.Status);
        await periodLock.EnsureOpenAsync(invoice.Date, ct);

        var cari = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CurrentAccountId, ct)
                   ?? throw new NotFoundException(invoice.CurrentAccountId.ToString(), "CurrentAccount");
        InvoiceBuilder.EnsureCari(cari, invoice.Kind);

        await using var tx = ownTransaction ? await db.BeginTransactionAsync(ct) : null;
        try {
            // İadeler aynı asıl faturanın cari satırında sıraya girer: iki paralel kesim iade miktarını birlikte aşamaz.
            await db.LockCurrentAccountAsync(invoice.CurrentAccountId, ct);

            // Kilidi beklerken başka bir istek faturayı kesmiş ya da silmiş olabilir: durumu kilit altında yeniden oku.
            var current = await db.Invoices.AsNoTracking().Where(i => i.Id == invoice.Id).Select(i => (InvoiceStatus?)i.Status).FirstOrDefaultAsync(ct);
            if (current is null) throw new NotFoundException(invoiceId.ToString(), "Invoice");
            if (current != InvoiceStatus.Draft) throw InvoiceErrors.NotDraft(current.Value);

            // Taslağın hazırlandığı andan beri asıl fatura iptal edilmiş ya da başka iadeler kesilmiş olabilir (iptal de aynı cari kilidini alır).
            if (InvoiceRules.IsReturn(invoice.Kind) && invoice.ReturnOfInvoiceId is { } originalId) {
                var originalStatus = await db.Invoices.AsNoTracking().Where(i => i.Id == originalId).Select(i => (InvoiceStatus?)i.Status).FirstOrDefaultAsync(ct);
                if (originalStatus is null) throw new NotFoundException(originalId.ToString(), "Invoice");
                if (originalStatus == InvoiceStatus.Cancelled) throw InvoiceErrors.Conflict("original_cancelled", "Asıl fatura iptal edilmiş; iade faturası kesilemez.");
                if (originalStatus != InvoiceStatus.Issued) throw InvoiceErrors.Conflict("return_of_not_issued", "İade yalnızca kesilmiş faturaya yapılabilir.");
                await builder.EnsureReturnQuantitiesAsync(originalId, invoice.Lines, invoice.Id, ct);
            }

            // Belge kilitleri (sipariş vb.) ve vade planı hazırlığı: seri numarası ve stok kilitlerinden önce.
            await poster.BeforeIssueAsync(invoice, ct);

            var number = reserved ?? await ReserveNumberAsync(invoice.BranchId, invoice.Kind, invoice.Date, cari, ct);
            invoice.SeriesId      = number.Series.Id;
            invoice.Number        = number.Series.Format(number.Sequence);
            invoice.Mode          = number.Mode;
            invoice.Scenario      = number.Scenario;
            invoice.BuyerOrSeller = await SnapshotAsync(cari, ct);
            invoice.Time        ??= TimeOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
            invoice.Status        = InvoiceStatus.Issued;
            invoice.LastModified  = clock.GetUtcNow();

            // Defter, vade ve stok kayıtları numarayla aynı işlemde yazılır; biri başarısız olursa numara dahil hiçbiri kalmaz.
            await poster.PostAsync(invoice, ct);

            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw InvoiceErrors.NotDraft(InvoiceStatus.Issued);
        } catch (DbUpdateException ex) when (InvoicingErrors.IsUniqueViolation(ex)) {
            db.ClearTracking();
            throw InvoiceErrors.Conflict("number_conflict", "Bu fatura numarası başka bir belgede kullanılıyor; seri önekini değiştirin.");
        }

        return (invoice, cari.Name);
    }

    private async Task<AddressSnapshot> SnapshotAsync(CurrentAccount cari, CancellationToken ct) {
        var addresses = await db.CurrentAccountAddresses.AsNoTracking().Where(a => a.CurrentAccountId == cari.Id).ToListAsync(ct);
        var address   = addresses.FirstOrDefault(a => a.IsDefaultBilling) ?? addresses.FirstOrDefault(a => a.IsBilling) ?? addresses.FirstOrDefault();
        return new AddressSnapshot(cari.Name, cari.TaxNumber, cari.TaxOffice, address?.Line, address?.City, address?.District, address?.Country, cari.Email);
    }
}
