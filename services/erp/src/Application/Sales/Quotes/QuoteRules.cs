using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Quotes;

public static class QuoteRules {
    public static async Task<Quote> LoadAsync(IApplicationDbContext db, ICurrentTenant tenant, Guid id, bool track, CancellationToken ct) {
        var query = db.Quotes.Include(q => q.Revisions).ThenInclude(r => r.Lines).AsQueryable();
        if (!track) query = query.AsNoTracking();
        var quote = await query.FirstOrDefaultAsync(q => q.Id == id, ct) ?? throw new NotFoundException(id.ToString(), "Quote");
        BranchGuard.Ensure(tenant, quote.BranchId);
        return quote;
    }

    public static QuoteRevision RevisionOf(Quote quote, int? revision) {
        var number = revision ?? quote.CurrentRevision;
        return quote.Revisions.FirstOrDefault(r => r.Revision == number) ?? throw new NotFoundException(number.ToString(), "QuoteRevision");
    }
}

/// <summary>Girdiyi doğrulayıp teklif başlığına ve verilen revizyona yazar (satırlar, toplamlar, temsilci, geçerlilik).</summary>
public sealed class QuoteWriter(
    SalesLineBuilder builder,
    ITenantSettings  settings
) {
    public async Task ApplyAsync(Quote quote, QuoteRevision revision, QuoteInput input, CancellationToken ct) {
        await builder.EnsureBranchAsync(input.BranchId, ct);
        var customer = await builder.LoadCustomerAsync(input.CurrentAccountId, ct);
        var (repId, repName) = await builder.ResolveRepAsync(input.SalesRepUserId, customer, ct);
        var currency = input.Currency.Trim().ToUpperInvariant();
        var document = await builder.BuildAsync(customer, currency, input.Date, input.DocDiscount, input.Lines, ct);

        var validity = (await settings.GetAsync<SalesSettings>(ct)).QuoteValidityDays;
        quote.CurrentAccountId = customer.Id;
        quote.BranchId         = input.BranchId;
        quote.Currency         = currency;
        quote.SalesRepUserId   = repId;
        quote.SalesRepName     = repName;

        revision.QuoteId     = quote.Id;
        revision.Date        = input.Date;
        revision.ValidUntil  = input.ValidUntil ?? input.Date.AddDays(validity);
        revision.DocDiscount = input.DocDiscount ?? 0m;
        revision.Notes       = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        revision.Gross       = document.Calc.Gross;
        revision.Discount    = document.Calc.Discount;
        revision.Net         = document.Calc.Net;
        revision.Vat         = document.Calc.Vat;
        revision.Total       = document.Calc.Payable;

        foreach (var l in document.Lines)
            revision.Lines.Add(new QuoteLine {
                QuoteRevisionId = revision.Id, Sort = l.Sort, ProductId = l.ProductId, VariantId = l.VariantId, Description = l.Description, Unit = l.Unit,
                Factor = l.Factor, Quantity = l.Quantity, Width = l.Width, Length = l.Length, UnitPrice = l.UnitPrice, VatIncluded = l.VatIncluded,
                DiscountPercent = l.DiscountPercent, VatRate = l.VatRate, SupplyMode = l.SupplyMode, SupplierId = l.SupplierId, Net = l.Net, Vat = l.Vat, Total = l.Total
            });
    }
}

public sealed class QuoteMapper(
    IApplicationDbContext db
) {
    public async Task<QuoteDto> ToDtoAsync(Quote quote, int? revisionNumber, CancellationToken ct) {
        var rev      = QuoteRules.RevisionOf(quote, revisionNumber);
        var customer = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == quote.CurrentAccountId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var ids      = rev.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);

        var lines = rev.Lines.OrderBy(l => l.Sort).Select(l => {
            var info = products.GetValueOrDefault(l.ProductId);
            return new QuoteLineDto(l.Id, l.Sort, l.ProductId, info?.Code ?? string.Empty, info?.Name ?? l.Description, l.VariantId, l.Description, l.Unit, l.Factor,
                                    l.Quantity, l.Width, l.Length, l.UnitPrice, l.VatIncluded, l.DiscountPercent, l.VatRate, l.SupplyMode, l.SupplierId, l.Net, l.Vat, l.Total);
        }).ToList();
        var revisions = quote.Revisions.OrderBy(r => r.Revision).Select(r => new QuoteRevisionRefDto(r.Revision, r.Date, r.ValidUntil, r.Total)).ToList();

        return new QuoteDto(quote.Id, quote.Number, quote.Status, quote.CurrentAccountId, customer, quote.BranchId, quote.Currency, quote.SalesRepUserId, quote.SalesRepName,
                            quote.CurrentRevision, rev.Revision, rev.Date, rev.ValidUntil, rev.DocDiscount, rev.Notes, quote.SentAt, quote.RejectReason, quote.SalesOrderId,
                            rev.Gross, rev.Discount, rev.Net, rev.Vat, rev.Total, quote.Version, lines, revisions);
    }
}
