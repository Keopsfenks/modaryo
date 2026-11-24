using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Documents;

/// <summary>Kesilmiş faturayı PDF ekiyle gönderir. Taslakta 409 invoice_not_issued, iptalde 409 invoice_cancelled.</summary>
[Authorize(Permission = InvoicingPermissions.Issue)]
public sealed record SendInvoiceEmailCommand(Guid Id, List<string> To, List<string>? Cc, string? Subject, string? Body) : IRequest;

public sealed partial class SendInvoiceEmailCommandValidator : AbstractValidator<SendInvoiceEmailCommand> {
    public SendInvoiceEmailCommandValidator() {
        RuleFor(c => c.To).Must(t => t is { Count: >= 1 and <= 20 }).WithMessage("En az bir, en çok 20 alıcı adresi verilmelidir.");
        RuleForEach(c => c.To).Must(IsAddress).WithMessage("Geçersiz e-posta adresi.");
        RuleFor(c => c.Cc).Must(c => c is null || c.Count <= 20).WithMessage("En çok 20 bilgi (cc) adresi verilebilir.");
        RuleForEach(c => c.Cc).Must(IsAddress).WithMessage("Geçersiz e-posta adresi.");
        RuleFor(c => c.Subject).MaximumLength(200).WithMessage("Konu en fazla 200 karakter olabilir.");
        RuleFor(c => c.Body).MaximumLength(2000).WithMessage("Metin en fazla 2000 karakter olabilir.");
    }

    private static bool IsAddress(string? address) {
        return address is { Length: <= 254 } && Address().IsMatch(address.Trim());
    }

    // Boşluk/denetim karakteri, görünen ad ("Ad <a@b>"), liste ayırıcı ve tırnak içermeyen yalın adres (başlık enjeksiyonuna karşı).
    [GeneratedRegex(@"^[^\s@<>,;""()\[\]\\]+@[^\s@<>,;""()\[\]\\]+\.[^\s@<>,;""()\[\]\\]+$")]
    private static partial Regex Address();
}

public sealed class SendInvoiceEmailCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    ITenantSettings       settings,
    IDocumentPdf          pdf,
    IDocumentMailer       mailer
) : IRequestHandler<SendInvoiceEmailCommand> {
    public async Task Handle(SendInvoiceEmailCommand request, CancellationToken ct) {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.DueSchedule).FirstOrDefaultAsync(i => i.Id == request.Id, ct)
                      ?? throw new NotFoundException(request.Id.ToString(), "Invoice");
        InvoiceRules.EnsureKindPermission(session, invoice.Kind);
        BranchGuard.Ensure(tenant, invoice.BranchId);
        if (invoice.Status == InvoiceStatus.Cancelled) throw InvoiceErrors.Conflict("invoice_cancelled", "İptal edilmiş fatura gönderilemez.");
        if (invoice.Status != InvoiceStatus.Issued || invoice.Number is null)
            throw InvoiceErrors.Conflict("invoice_not_issued", "Yalnızca kesilmiş faturalar e-posta ile gönderilebilir.");

        var cariName = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == invoice.CurrentAccountId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var firm     = await settings.GetAsync<CompanyProfileSettings>(ct);
        var document = await settings.GetAsync<DocumentSettings>(ct);

        var due = invoice.DueSchedule.Count > 0 ? invoice.DueSchedule.Min(d => d.DueDate) : invoice.Date.AddDays(invoice.PaymentTermDays);
        var variables = new Dictionary<string, string> {
                                                           ["firma"]   = firm.LegalName ?? string.Empty,
                                                           ["belgeNo"] = invoice.Number,
                                                           ["alici"]   = cariName,
                                                           ["tutar"]   = InvoiceMail.Money(invoice.Payable, invoice.Currency),
                                                           ["vade"]    = due.ToString("dd.MM.yyyy", InvoiceMail.Tr)
                                                       };

        var subject = InvoiceMail.Fill(string.IsNullOrWhiteSpace(request.Subject) ? document.InvoiceEmailSubject : request.Subject, variables)
                                 .ReplaceLineEndings(" ").Trim();
        var body = InvoiceMail.ToHtml(InvoiceMail.Fill(string.IsNullOrWhiteSpace(request.Body) ? document.InvoiceEmailBody : request.Body, variables));

        var content = await pdf.RenderInvoiceAsync(invoice.Id, ct);
        await mailer.SendAsync(request.To.Select(a => a.Trim()).ToList(), request.Cc?.Select(a => a.Trim()).ToList(), subject, body,
                               [(InvoiceMail.FileName(invoice.Number, invoice.Id), content, "application/pdf")], ct);
    }
}

/// <summary>PDF ve e-posta ortak yardımcıları: Türkçe biçim, şablon değişkenleri, dosya adı.</summary>
public static partial class InvoiceMail {
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>"1.234,56 TL" (TRY için TL, diğerlerinde para birimi kodu).</summary>
    public static string Money(decimal amount, string currency) {
        return amount.ToString("N2", Tr) + " " + (currency == "TRY" ? "TL" : currency);
    }

    /// <summary>{firma} {belgeNo} {alici} {tutar} {vade} değişkenlerini tek geçişte doldurur (yerine konan değer yeniden genişletilmez).</summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> variables) {
        return Variable().Replace(template, m => variables.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
    }

    /// <summary>Metni düz metin sayar: HTML kaçırılır, satır sonları &lt;br&gt; olur.</summary>
    public static string ToHtml(string text) {
        var encoded = Html.Encode(text).ReplaceLineEndings("\n").Replace("\n", "<br>\n");
        return $"<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.5\">{encoded}</div>";
    }

    public static string FileName(string? number, Guid id) {
        var stem = string.IsNullOrWhiteSpace(number) ? "taslak-" + id.ToString("N")[..8] : number;
        stem = string.Concat(stem.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));
        return stem + ".pdf";
    }

    [GeneratedRegex(@"\{(firma|belgeNo|alici|tutar|vade)\}")]
    private static partial Regex Variable();
}
