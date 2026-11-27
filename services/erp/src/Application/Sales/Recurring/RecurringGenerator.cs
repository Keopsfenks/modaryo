using ERP.Application.Common.Interfaces;
using ERP.Application.Invoicing;
using ERP.Application.Pricing.Quotes;
using ERP.Domain.Invoicing;
using ERP.Domain.Pricing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Recurring;

/// <summary>
///     Tek bir (şablon, dönem) için fatura üretir. İdempotenttir ve kendisiyle yarışsa da aynı dönemi iki kez üretmez:
///     her deneme tek işlemde şablon satırını kilitler (FOR UPDATE), kilit altında şablonun hâlâ etkin olduğunu ve dönemin üretilmediğini
///     yeniden okur, faturayı (otomatik kesimde kesimi de) ve <see cref="RecurringRun" /> kaydını aynı işlemde yazar. (şablon, dönem) tekil dizini yalnızca
///     son güvencedir. Çağıran her deneme için yeni bir DI kapsamı ve firma bağlamı açar; hata olursa işlem geri alınır ve sonraki tur yeniden dener.
///     Taslak ile kesim ayrı işlemlere bölünmez: kesim başarısız olursa dönem de üretilmemiş sayılır.
/// </summary>
public sealed class RecurringGenerator(
    IApplicationDbContext db,
    InvoiceBuilder        builder,
    InvoiceIssuer         issuer,
    IPriceQuoteService    quotes,
    TimeProvider          clock
) {
    /// <summary>Üretilen faturanın kimliği; dönem zaten üretilmişse, şablon pasifse ya da dönem şablonun aralığında değilse null.</summary>
    public async Task<Guid?> GenerateAsync(Guid templateId, DateOnly period, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockRecurringTemplateAsync(templateId, ct)) return null;

        // Kilit altında yeniden oku: kilidi beklerken şablon duraklatılmış, değiştirilmiş ya da dönem başka bir tur tarafından üretilmiş olabilir.
        var template = await db.RecurringTemplates.AsNoTracking().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null || !template.IsActive) return null;
        if (period < template.StartDate || (template.EndDate is { } end && period > end)) return null;
        if (!RecurrenceCalendar.DueDates(template.Interval, template.DayOfMonth, template.StartDate, template.EndDate, period).Contains(period)) return null;
        if (await db.RecurringRuns.AsNoTracking().AnyAsync(r => r.RecurringTemplateId == templateId && r.Period == period, ct)) return null;

        var lines = new List<LineInput>();
        foreach (var line in template.Lines.OrderBy(l => l.Sort)) {
            var (price, vatIncluded) = await PriceAsync(template, line, period, ct);
            lines.Add(new LineInput {
                ProductId = line.ProductId, VariantId = line.VariantId, Description = line.Description, Unit = line.Unit, Quantity = line.Quantity, UnitPrice = price,
                VatIncluded = vatIncluded, DiscountPercent = line.DiscountPercent, VatRate = line.VatRate
            });
        }

        var input = new InvoiceInput {
            Kind = InvoiceKind.Sales, BranchId = template.BranchId, CurrentAccountId = template.CurrentAccountId, Date = period, Currency = template.Currency, AffectsStock = false,
            Notes = $"Tekrarlayan fatura {template.Number} - {period:dd.MM.yyyy}", Lines = lines
        };
        var built   = await builder.BuildAsync(input, null, false, ct, true);
        var invoice = new Invoice { Status = InvoiceStatus.Draft, SourceType = RecurringRules.Source, SourceId = template.Id };
        InvoiceBuilder.Apply(invoice, input, built);
        foreach (var line in built.Lines) {
            line.InvoiceId    = invoice.Id;
            line.SourceLineId = null;
            invoice.Lines.Add(line);
        }

        foreach (var due in built.Due) {
            due.InvoiceId = invoice.Id;
            invoice.DueSchedule.Add(due);
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        if (template.Mode == RecurringMode.AutoIssue) await issuer.IssueAsync(invoice.Id, false, ct);

        db.RecurringRuns.Add(new RecurringRun { RecurringTemplateId = template.Id, Period = period, InvoiceId = invoice.Id, GeneratedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return invoice.Id;
    }

    /// <summary>Sabit satır (ya da kalemsiz satır) kayıtlı fiyatla; diğerleri üretim gününde fiyat listesinden yeniden fiyatlanır. Fiyat bulunamaz ya da para birimi uyuşmazsa kayıtlı fiyat kullanılır.</summary>
    private async Task<(decimal Price, bool VatIncluded)> PriceAsync(RecurringTemplate template, RecurringTemplateLine line, DateOnly period, CancellationToken ct) {
        if (line.IsFixedPrice || line.ProductId is not { } productId) return (line.UnitPrice, line.VatIncluded);

        var quote = await quotes.QuoteAsync(new QuoteRequest(PriceListKind.Sale, template.CurrentAccountId, productId, line.VariantId, line.Unit, line.Quantity, period), ct);
        return quote is not null && string.Equals(quote.Currency, template.Currency, StringComparison.OrdinalIgnoreCase) ? (quote.UnitPrice, quote.VatIncluded) : (line.UnitPrice, line.VatIncluded);
    }
}

/// <summary>Nakit akışı tahmini: üretilmemiş dönemlerin tutarı (kayıtlı fiyatlarla; yeniden fiyatlanacak satırlar için tahmin).</summary>
public sealed class RecurringForecast(
    IApplicationDbContext db
) : Treasury.IRecurringForecast {
    public async Task<IReadOnlyList<Treasury.RecurringForecastItem>> UpcomingAsync(DateOnly from, DateOnly to, CancellationToken ct) {
        if (to < from) return [];

        var templates = await db.RecurringTemplates.AsNoTracking().Include(t => t.Lines).Where(t => t.IsActive && t.StartDate <= to && (t.EndDate == null || t.EndDate >= from)).ToListAsync(ct);
        var ids       = templates.Select(t => t.Id).ToList();
        var runs      = (await db.RecurringRuns.AsNoTracking().Where(r => ids.Contains(r.RecurringTemplateId) && r.Period >= from && r.Period <= to).Select(r => new { r.RecurringTemplateId, r.Period }).ToListAsync(ct))
                        .Select(r => (r.RecurringTemplateId, r.Period)).ToHashSet();
        var items = new List<Treasury.RecurringForecastItem>();
        foreach (var template in templates) {
            var amount = RecurringRules.Totals(template.Lines).Payable;
            if (amount <= 0m) continue;

            foreach (var date in RecurrenceCalendar.DueDates(template.Interval, template.DayOfMonth, template.StartDate, template.EndDate, to).Where(d => d >= from && !runs.Contains((template.Id, d))))
                items.Add(new Treasury.RecurringForecastItem(template.Id, date, amount, template.Currency));
        }

        return items;
    }
}
