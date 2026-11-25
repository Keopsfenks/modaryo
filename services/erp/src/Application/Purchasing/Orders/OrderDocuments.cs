using System.Text.RegularExpressions;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing.Documents;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Orders;

/// <summary>Sipariş PDF'i. Alış fiyatı yetkisi yoksa fiyat sütunları ve toplamlar basılmaz.</summary>
[Authorize(Permission = PurchasingPermissions.Read)]
public sealed record GetPoPdfQuery(Guid Id) : IRequest<PoPdfFile>;

public sealed class GetPoPdfQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    IDocumentPdf          pdf
) : IRequestHandler<GetPoPdfQuery, PoPdfFile> {
    public async Task<PoPdfFile> Handle(GetPoPdfQuery request, CancellationToken ct) {
        var po = await PoRules.LoadAsync(db, tenant, request.Id, false, ct);
        var content = await pdf.RenderPurchaseOrderAsync(po.Id, session.HasPermission(PurchasingPermissions.PurchasePrice), ct);
        return new PoPdfFile(content, InvoiceMail.FileName(po.Number, po.Id));
    }
}

/// <summary>
///     Siparişi PDF ekiyle tedarikçiye gönderir. Alıcı verilmezse tedarikçi kartındaki e-posta kullanılır (yoksa 400 no_recipient).
///     Taslak ve iptal edilmiş sipariş gönderilemez.
/// </summary>
[Authorize(Permission = PurchasingPermissions.Manage)]
public sealed record SendPoEmailCommand(Guid Id, List<string>? To, List<string>? Cc, string? Subject, string? Body) : IRequest;

public sealed partial class SendPoEmailCommandValidator : AbstractValidator<SendPoEmailCommand> {
    public SendPoEmailCommandValidator() {
        RuleFor(c => c.To).Must(t => t is null || t.Count <= 20).WithMessage("En çok 20 alıcı adresi verilebilir.");
        RuleForEach(c => c.To).Must(IsAddress).WithMessage("Geçersiz e-posta adresi.");
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

public sealed class SendPoEmailCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    ITenantSettings       settings,
    IDocumentPdf          pdf,
    IDocumentMailer       mailer
) : IRequestHandler<SendPoEmailCommand> {
    public async Task Handle(SendPoEmailCommand request, CancellationToken ct) {
        var po = await PoRules.LoadAsync(db, tenant, request.Id, false, ct);
        if (po.Status == PurchaseOrderStatus.Cancelled) throw PurchasingErrors.Conflict("po_cancelled", "İptal edilmiş sipariş gönderilemez.");
        if (po.Status == PurchaseOrderStatus.Draft) throw PurchasingErrors.Conflict("po_not_approved", "Yalnızca onaylanmış siparişler e-posta ile gönderilebilir.");

        var supplier = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == po.SupplierId).Select(c => new { c.Name, c.Email }).FirstAsync(ct);
        var to = request.To is { Count: > 0 } given ? given.Select(a => a.Trim()).ToList()
                 : string.IsNullOrWhiteSpace(supplier.Email) ? [] : [supplier.Email.Trim()];
        if (to.Count == 0) throw PurchasingErrors.Bad("no_recipient", "Alıcı adresi verilmedi ve tedarikçi kartında e-posta yok.");

        var firm = await settings.GetAsync<CompanyProfileSettings>(ct);
        var variables = new Dictionary<string, string> {
            ["firma"]   = firm.LegalName ?? string.Empty,
            ["belgeNo"] = po.Number,
            ["alici"]   = supplier.Name,
            ["tutar"]   = string.Empty,
            ["vade"]    = string.Empty
        };
        var subject = InvoiceMail.Fill(string.IsNullOrWhiteSpace(request.Subject) ? "{firma} - {belgeNo} numaralı satın alma siparişi" : request.Subject, variables)
                                 .ReplaceLineEndings(" ").Trim();
        var body = InvoiceMail.ToHtml(InvoiceMail.Fill(string.IsNullOrWhiteSpace(request.Body) ? "Sayın {alici}, {belgeNo} numaralı satın alma siparişimiz ektedir." : request.Body,
                                                       variables));

        var content = await pdf.RenderPurchaseOrderAsync(po.Id, session.HasPermission(PurchasingPermissions.PurchasePrice), ct);
        await mailer.SendAsync(to, request.Cc?.Select(a => a.Trim()).ToList(), subject, body,
                               [(InvoiceMail.FileName(po.Number, po.Id), content, "application/pdf")], ct);
    }
}
