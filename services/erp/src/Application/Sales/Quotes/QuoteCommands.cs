using System.Text.RegularExpressions;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Inventory.Documents;
using ERP.Application.Invoicing.Documents;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Quotes;

[Authorize(Permission = SalesPermissions.Manage)]
public sealed record CreateQuoteCommand(QuoteInput Input) : IRequest<QuoteDto>;

public sealed class CreateQuoteCommandValidator : AbstractValidator<CreateQuoteCommand> {
    public CreateQuoteCommandValidator() {
        RuleFor(c => c.Input).NotNull().SetValidator(new QuoteInputValidator());
    }
}

public sealed class CreateQuoteCommandHandler(
    IApplicationDbContext db,
    QuoteWriter           writer,
    QuoteMapper           mapper
) : IRequestHandler<CreateQuoteCommand, QuoteDto> {
    public async Task<QuoteDto> Handle(CreateQuoteCommand request, CancellationToken ct) {
        var quote    = new Quote { Status = QuoteStatus.Draft, CurrentRevision = 1 };
        var revision = new QuoteRevision { QuoteId = quote.Id, Revision = 1 };
        quote.Revisions.Add(revision);
        await writer.ApplyAsync(quote, revision, request.Input, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        quote.Number = await DocumentNumbers.NextAsync(db, "TK", revision.Date, ct);
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(quote, null, ct);
    }
}

/// <summary>Taslak teklif yerinde güncellenir; gönderilmiş teklifte yeni revizyon açılır (eski revizyonlar salt okunur kalır) ve teklif taslağa döner.</summary>
[Authorize(Permission = SalesPermissions.Manage)]
public sealed record UpdateQuoteCommand(Guid Id, QuoteInput Input) : IRequest<QuoteDto>;

public sealed class UpdateQuoteCommandValidator : AbstractValidator<UpdateQuoteCommand> {
    public UpdateQuoteCommandValidator() {
        RuleFor(c => c.Input).NotNull().SetValidator(new QuoteInputValidator());
    }
}

public sealed class UpdateQuoteCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    QuoteWriter           writer,
    QuoteMapper           mapper,
    TimeProvider          clock
) : IRequestHandler<UpdateQuoteCommand, QuoteDto> {
    public async Task<QuoteDto> Handle(UpdateQuoteCommand request, CancellationToken ct) {
        var quote = await QuoteRules.LoadAsync(db, tenant, request.Id, true, ct);
        if (quote.Status is not (QuoteStatus.Draft or QuoteStatus.Sent)) throw SalesErrors.QuoteNotEditable();
        if (request.Input.Version is { } version && version != quote.Version) throw SalesErrors.VersionConflict();

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            QuoteRevision revision;
            if (quote.Status == QuoteStatus.Draft) {
                revision = QuoteRules.RevisionOf(quote, null);
                db.QuoteLines.RemoveRange(revision.Lines);
                revision.Lines.Clear();
                await db.SaveChangesAsync(ct);
            } else {
                revision = new QuoteRevision { QuoteId = quote.Id, Revision = quote.CurrentRevision + 1 };
                quote.Revisions.Add(revision);
                quote.CurrentRevision = revision.Revision;
                quote.Status          = QuoteStatus.Draft;
                quote.SentAt          = null;
            }

            await writer.ApplyAsync(quote, revision, request.Input, ct);
            quote.LastModified = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw SalesErrors.VersionConflict();
        }

        return await mapper.ToDtoAsync(quote, null, ct);
    }
}

[Authorize(Permission = SalesPermissions.Manage)]
public sealed record SendQuoteCommand(Guid Id, List<string>? EmailTo, List<string>? Cc, string? Subject, string? Body) : IRequest<QuoteDto>;

public sealed partial class SendQuoteCommandValidator : AbstractValidator<SendQuoteCommand> {
    public SendQuoteCommandValidator() {
        RuleFor(c => c.EmailTo).Must(t => t is null || t.Count <= 20).WithMessage("En çok 20 alıcı adresi verilebilir.");
        RuleForEach(c => c.EmailTo).Must(IsAddress).WithMessage("Geçersiz e-posta adresi.");
        RuleFor(c => c.Cc).Must(c => c is null || c.Count <= 20).WithMessage("En çok 20 bilgi (cc) adresi verilebilir.");
        RuleForEach(c => c.Cc).Must(IsAddress).WithMessage("Geçersiz e-posta adresi.");
        RuleFor(c => c.Subject).MaximumLength(200).WithMessage("Konu en fazla 200 karakter olabilir.");
        RuleFor(c => c.Body).MaximumLength(2000).WithMessage("Metin en fazla 2000 karakter olabilir.");
    }

    private static bool IsAddress(string? address) {
        return address is { Length: <= 254 } && Address().IsMatch(address.Trim());
    }

    [GeneratedRegex(@"^[^\s@<>,;""()\[\]\\]+@[^\s@<>,;""()\[\]\\]+\.[^\s@<>,;""()\[\]\\]+$")]
    private static partial Regex Address();
}

/// <summary>Taslak teklifi gönderildi durumuna alır; alıcı verilirse PDF ekiyle e-posta da gönderir.</summary>
public sealed class SendQuoteCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ITenantSettings       settings,
    IDocumentPdf          pdf,
    IDocumentMailer       mailer,
    QuoteMapper           mapper,
    TimeProvider          clock
) : IRequestHandler<SendQuoteCommand, QuoteDto> {
    public async Task<QuoteDto> Handle(SendQuoteCommand request, CancellationToken ct) {
        var quote = await QuoteRules.LoadAsync(db, tenant, request.Id, true, ct);
        if (quote.Status != QuoteStatus.Draft) throw SalesErrors.QuoteNotEditable();

        quote.Status       = QuoteStatus.Sent;
        quote.SentAt       = clock.GetUtcNow();
        quote.LastModified = quote.SentAt.Value;
        await db.SaveChangesAsync(ct);

        if (request.EmailTo is { Count: > 0 } to) {
            var customer = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == quote.CurrentAccountId).Select(c => c.Name).FirstAsync(ct);
            var firm     = await settings.GetAsync<CompanyProfileSettings>(ct);
            var variables = new Dictionary<string, string> {
                ["firma"] = firm.LegalName ?? string.Empty, ["belgeNo"] = quote.Number, ["alici"] = customer,
                ["tutar"] = QuoteRules.RevisionOf(quote, null).Total.ToString("N2", InvoiceMail.Tr) + " " + quote.Currency,
                ["vade"]  = QuoteRules.RevisionOf(quote, null).ValidUntil?.ToString("dd.MM.yyyy", InvoiceMail.Tr) ?? string.Empty
            };
            var subject = InvoiceMail.Fill(string.IsNullOrWhiteSpace(request.Subject) ? "{firma} - {belgeNo} numaralı teklif" : request.Subject, variables)
                                     .ReplaceLineEndings(" ").Trim();
            var body = InvoiceMail.ToHtml(InvoiceMail.Fill(string.IsNullOrWhiteSpace(request.Body) ? "Sayın {alici}, {belgeNo} numaralı teklifimiz ektedir. Geçerlilik: {vade}." : request.Body,
                                                           variables));
            var content = await pdf.RenderQuoteAsync(quote.Id, null, ct);
            await mailer.SendAsync(to.Select(a => a.Trim()).ToList(), request.Cc?.Select(a => a.Trim()).ToList(), subject, body,
                                   [(InvoiceMail.FileName(quote.Number, quote.Id), content, "application/pdf")], ct);
        }

        return await mapper.ToDtoAsync(quote, null, ct);
    }
}

[Authorize(Permission = SalesPermissions.Manage)]
public sealed record AcceptQuoteCommand(Guid Id) : IRequest<AcceptQuoteResult>;

/// <summary>Gönderilmiş teklifi kabul eder ve aynı satırlarla taslak sipariş üretir (ConvertToOrder).</summary>
public sealed class AcceptQuoteCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    TimeProvider          clock
) : IRequestHandler<AcceptQuoteCommand, AcceptQuoteResult> {
    public async Task<AcceptQuoteResult> Handle(AcceptQuoteCommand request, CancellationToken ct) {
        var quote = await QuoteRules.LoadAsync(db, tenant, request.Id, true, ct);
        if (quote.Status != QuoteStatus.Sent) throw SalesErrors.QuoteNotEditable();
        var revision = QuoteRules.RevisionOf(quote, null);
        if (revision.ValidUntil is { } until && until < CurrentAccountClock.Today(clock))
            throw SalesErrors.Conflict("quote_expired", "Teklifin geçerlilik süresi dolmuş.");

        await using var tx = await db.BeginTransactionAsync(ct);
        var order = new SalesOrder {
            Status = SalesOrderStatus.Draft, CurrentAccountId = quote.CurrentAccountId, BranchId = quote.BranchId, Date = CurrentAccountClock.Today(clock),
            Currency = quote.Currency, SalesRepUserId = quote.SalesRepUserId, SalesRepName = quote.SalesRepName, QuoteId = quote.Id, QuoteRevision = revision.Revision,
            DocDiscount = revision.DocDiscount, Notes = revision.Notes, Gross = revision.Gross, Discount = revision.Discount, Net = revision.Net, Vat = revision.Vat,
            Total = revision.Total
        };
        foreach (var l in revision.Lines.OrderBy(l => l.Sort))
            order.Lines.Add(new SalesOrderLine {
                SalesOrderId = order.Id, Sort = l.Sort, ProductId = l.ProductId, VariantId = l.VariantId, Description = l.Description, Unit = l.Unit, Factor = l.Factor,
                Quantity = l.Quantity, Width = l.Width, Length = l.Length, UnitPrice = l.UnitPrice, VatIncluded = l.VatIncluded, DiscountPercent = l.DiscountPercent,
                VatRate = l.VatRate, SupplyMode = l.SupplyMode, SupplierId = l.SupplierId, QuoteLineId = l.Id, Net = l.Net, Vat = l.Vat, Total = l.Total
            });
        order.Number = await DocumentNumbers.NextAsync(db, "SS", order.Date, ct);
        db.SalesOrders.Add(order);

        quote.Status       = QuoteStatus.Accepted;
        quote.SalesOrderId = order.Id;
        quote.LastModified = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new AcceptQuoteResult(order.Id, order.Number);
    }
}

[Authorize(Permission = SalesPermissions.Manage)]
public sealed record RejectQuoteCommand(Guid Id, string Reason) : IRequest<QuoteDto>;

public sealed class RejectQuoteCommandValidator : AbstractValidator<RejectQuoteCommand> {
    public RejectQuoteCommandValidator() {
        RuleFor(c => c.Reason).NotEmpty().WithMessage("Ret nedeni zorunludur.")
            .Length(2, 200).WithMessage("Ret nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

public sealed class RejectQuoteCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    QuoteMapper           mapper,
    TimeProvider          clock
) : IRequestHandler<RejectQuoteCommand, QuoteDto> {
    public async Task<QuoteDto> Handle(RejectQuoteCommand request, CancellationToken ct) {
        var quote = await QuoteRules.LoadAsync(db, tenant, request.Id, true, ct);
        if (quote.Status != QuoteStatus.Sent) throw SalesErrors.QuoteNotEditable();
        quote.Status       = QuoteStatus.Rejected;
        quote.RejectReason = request.Reason.Trim();
        quote.LastModified = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await mapper.ToDtoAsync(quote, null, ct);
    }
}
