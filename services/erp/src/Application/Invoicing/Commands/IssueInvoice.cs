using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing.Posting;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Commands;

[Authorize(Permission = InvoicingPermissions.Issue)]
public sealed record IssueInvoiceCommand(Guid Id) : IRequest<InvoiceDto>;

public sealed class IssueInvoiceCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    IPeriodLock           periodLock,
    ITenantSettings       settings,
    InvoiceBuilder        builder,
    InvoicePoster         poster,
    TimeProvider          clock
) : IRequestHandler<IssueInvoiceCommand, InvoiceDto> {
    public async Task<InvoiceDto> Handle(IssueInvoiceCommand request, CancellationToken ct) {
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.DueSchedule).FirstOrDefaultAsync(i => i.Id == request.Id, ct)
                      ?? throw new NotFoundException(request.Id.ToString(), "Invoice");
        InvoiceRules.EnsureKindPermission(session, invoice.Kind);
        BranchGuard.Ensure(tenant, invoice.BranchId);
        if (invoice.Status != InvoiceStatus.Draft) throw InvoiceErrors.NotDraft(invoice.Status);
        await periodLock.EnsureOpenAsync(invoice.Date, ct);

        var cari = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CurrentAccountId, ct)
                   ?? throw new NotFoundException(invoice.CurrentAccountId.ToString(), "CurrentAccount");
        InvoiceBuilder.EnsureCari(cari, invoice.Kind);

        var ebelge = await settings.GetAsync<EBelgeSettings>(ct);
        var mode   = !ebelge.Enabled ? InvoiceMode.Paper : cari.IsEInvoiceTaxpayer ? InvoiceMode.EInvoice : InvoiceMode.EArchive;

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            // İadeler aynı asıl faturanın cari satırında sıraya girer: iki paralel kesim iade miktarını birlikte aşamaz.
            await db.LockCurrentAccountAsync(invoice.CurrentAccountId, ct);

            // Kilidi beklerken başka bir istek faturayı kesmiş ya da silmiş olabilir: durumu kilit altında yeniden oku.
            var current = await db.Invoices.AsNoTracking().Where(i => i.Id == invoice.Id).Select(i => (InvoiceStatus?)i.Status).FirstOrDefaultAsync(ct);
            if (current is null) throw new NotFoundException(request.Id.ToString(), "Invoice");
            if (current != InvoiceStatus.Draft) throw InvoiceErrors.NotDraft(current.Value);

            // Taslağın hazırlandığı andan beri geçen sürede başka iadeler kesilmiş olabilir.
            if (InvoiceRules.IsReturn(invoice.Kind) && invoice.ReturnOfInvoiceId is { } originalId)
                await builder.EnsureReturnQuantitiesAsync(originalId, invoice.Lines, invoice.Id, ct);

            var series = await FindSeriesAsync(invoice, mode, ct);
            // UPDATE ... RETURNING: satır kilidi işlem sonuna dek tutulur; aynı seride paralel kesimler sıraya girer, geri alınan numara geri gelir.
            var sequence = await db.ReserveSeriesNumberAsync(series.Id, ct)
                           ?? throw InvoicingErrors.SeriesMissing();

            invoice.SeriesId      = series.Id;
            invoice.Number        = series.Format(sequence);
            invoice.Mode          = mode;
            invoice.Scenario      = mode == InvoiceMode.EInvoice ? ebelge.DefaultScenario : null;
            invoice.BuyerOrSeller = await SnapshotAsync(cari, ct);
            invoice.Time        ??= TimeOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
            invoice.Status        = InvoiceStatus.Issued;
            invoice.LastModified  = clock.GetUtcNow();

            // Defter, vade ve stok kayıtları numarayla aynı işlemde yazılır; biri başarısız olursa numara dahil hiçbiri kalmaz.
            await poster.PostAsync(invoice, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw InvoiceErrors.NotDraft(InvoiceStatus.Issued);
        } catch (DbUpdateException ex) when (InvoicingErrors.IsUniqueViolation(ex)) {
            db.ClearTracking();
            throw InvoiceErrors.Conflict("number_conflict", "Bu fatura numarası başka bir belgede kullanılıyor; seri önekini değiştirin.");
        }

        return InvoiceRules.ToDto(invoice, cari.Name);
    }

    /// <summary>
    ///     Varsayılan seri (grup, şube, mod); yılı belge yılıyla uyuşmuyorsa aynı öneke sahip o yılın aktif serisi.
    ///     Yoksa 409 series_missing.
    /// </summary>
    private async Task<DocumentSeries> FindSeriesAsync(Invoice invoice, InvoiceMode mode, CancellationToken ct) {
        var group = InvoiceRules.GroupOf(invoice.Kind);
        var year  = invoice.Date.Year;
        var def = await db.DocumentSeries.AsNoTracking()
                      .FirstOrDefaultAsync(s => s.IsDefault && s.IsActive && s.KindGroup == group && s.BranchId == invoice.BranchId && s.Mode == mode, ct);
        if (def is null) throw InvoicingErrors.SeriesMissing();
        if (def.Year == year) return def;

        return await db.DocumentSeries.AsNoTracking()
                   .FirstOrDefaultAsync(s => s.IsActive && s.KindGroup == group && s.BranchId == invoice.BranchId && s.Mode == mode
                                             && s.Prefix == def.Prefix && s.Year == year, ct)
               ?? throw InvoiceErrors.Conflict("series_missing", $"Varsayılan serinin {year} yılı için kaydı yok; {def.Prefix} önekiyle {year} serisi tanımlayın.");
    }

    private async Task<AddressSnapshot> SnapshotAsync(CurrentAccount cari, CancellationToken ct) {
        var addresses = await db.CurrentAccountAddresses.AsNoTracking().Where(a => a.CurrentAccountId == cari.Id).ToListAsync(ct);
        var address   = addresses.FirstOrDefault(a => a.IsDefaultBilling) ?? addresses.FirstOrDefault(a => a.IsBilling) ?? addresses.FirstOrDefault();
        return new AddressSnapshot(cari.Name, cari.TaxNumber, cari.TaxOffice, address?.Line, address?.City, address?.District, address?.Country, cari.Email);
    }
}
