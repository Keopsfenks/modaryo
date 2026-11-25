using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing;
using ERP.Domain.Invoicing;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Invoices.Commands;

public sealed record InvoiceDraftInput(IReadOnlyList<Guid> ReceiptIds, string ExternalNumber, DateOnly? Date);

/// <summary>
///     Bir ya da birkaç mal kabulden alış faturası taslağı. Satırlar mal kabul satırlarının henüz faturalanmamış miktarlarıdır
///     (iptal edilmemiş faturalar, taslaklar dahil, düşülür); fiyat sipariş satırından (iskonto ve KDV dahil) gelir. Taslak stok etkilemez
///     (AffectsStock = false; stok mal kabulde girmiştir). Kaynak "purchase-receipt", kaynak kimliği ilk mal kabul; her fatura satırı
///     kendi mal kabul satırına SourceLineId ile bağlanır. Kur belirtilmez: fatura tarihindeki kurla çözülür.
/// </summary>
[Authorize(Permission = PurchasingPermissions.Manage)]
[Authorize(Permission = InvoicingPermissions.Manage)]
[Authorize(Permission = PurchasingPermissions.PurchasePrice)]
public sealed record CreateInvoiceFromReceiptsCommand(InvoiceDraftInput Input) : IRequest<InvoiceDto>;

public sealed class CreateInvoiceFromReceiptsCommandValidator : AbstractValidator<CreateInvoiceFromReceiptsCommand> {
    public CreateInvoiceFromReceiptsCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.ReceiptIds).NotEmpty().WithMessage("En az bir mal kabul seçilmelidir.");
        RuleFor(c => c.Input.ExternalNumber).NotEmpty().WithMessage("Tedarikçinin fatura numarası zorunludur.").MaximumLength(50).WithMessage("Fatura numarası en fazla 50 karakter olabilir.");
    }
}

public sealed class CreateInvoiceFromReceiptsCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    InvoiceBuilder        builder,
    TimeProvider          clock
) : IRequestHandler<CreateInvoiceFromReceiptsCommand, InvoiceDto> {
    public async Task<InvoiceDto> Handle(CreateInvoiceFromReceiptsCommand request, CancellationToken ct) {
        var input = request.Input;
        var ids   = input.ReceiptIds.Distinct().ToList();
        var receipts = await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines).Where(r => ids.Contains(r.Id))
                               .OrderBy(r => r.Date).ThenBy(r => r.Number).ToListAsync(ct);
        if (receipts.Count != ids.Count) throw new NotFoundException(string.Join(",", ids.Except(receipts.Select(r => r.Id))), "GoodsReceipt");
        foreach (var r in receipts) BranchGuard.Ensure(tenant, r.BranchId);

        var first = receipts[0];
        if (receipts.Any(r => r.Status == GoodsReceiptStatus.Cancelled)) throw PurchasingErrors.Conflict("receipt_cancelled", "İptal edilmiş mal kabul faturalanamaz.");
        if (receipts.Any(r => r.SupplierId != first.SupplierId)) throw PurchasingErrors.Bad("supplier_mismatch", "Faturalanacak mal kabullerin tedarikçisi aynı olmalıdır.");
        if (receipts.Any(r => r.BranchId != first.BranchId)) throw PurchasingErrors.Bad("branch_mismatch", "Faturalanacak mal kabullerin şubesi aynı olmalıdır.");
        if (receipts.Any(r => r.Currency != first.Currency)) throw PurchasingErrors.Bad("currency_mismatch", "Faturalanacak mal kabullerin para birimi aynı olmalıdır.");

        // Faturalanmış (taslak dahil) temel miktar, mal kabul satırı başına.
        var invoiced = new Dictionary<Guid, decimal>();
        foreach (var invoice in await ReceiptInvoiceLinks.InvoicesAsync(db, ids, ct))
            foreach (var link in await ReceiptInvoiceLinks.ResolveAsync(db, invoice, false, ct))
                invoiced[link.ReceiptLine.Id] = invoiced.GetValueOrDefault(link.ReceiptLine.Id) + link.Line.Quantity * link.Line.Factor;

        var poLineIds = receipts.SelectMany(r => r.Lines).Where(l => l.PurchaseOrderLineId != null).Select(l => l.PurchaseOrderLineId!.Value).ToList();
        var poLines   = await db.PurchaseOrderLines.AsNoTracking().Where(l => poLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        var lines   = new List<LineInput>();
        var sources = new List<Guid>();
        foreach (var receipt in receipts) {
            foreach (var line in receipt.Lines.OrderBy(l => l.Sort)) {
                var open = line.BaseQuantity - invoiced.GetValueOrDefault(line.Id);
                if (open <= 0m || line.BaseQuantity <= 0m) continue;

                var quantity = line.Quantity * open / line.BaseQuantity;
                var poLine   = line.PurchaseOrderLineId is { } pid ? poLines.GetValueOrDefault(pid) : null;
                decimal price;
                decimal? discount = null;
                int? vat = null;
                if (poLine is not null) {
                    price    = poLine.UnitPrice / poLine.Factor * (line.BaseQuantity / line.Quantity);
                    discount = poLine.DiscountPercent;
                    vat      = poLine.VatRate;
                } else {
                    price = line.UnitPrice ?? 0m;
                }

                lines.Add(new LineInput {
                    ProductId = line.ProductId, VariantId = line.VariantId, Unit = line.Unit, Quantity = Math.Round(quantity, 6, MidpointRounding.AwayFromZero),
                    UnitPrice = Math.Round(price, 6, MidpointRounding.AwayFromZero), DiscountPercent = discount, VatRate = vat
                });
                sources.Add(line.Id);
            }
        }

        if (lines.Count == 0) throw PurchasingErrors.Conflict("nothing_to_invoice", "Seçilen mal kabuller zaten faturalanmış.");

        var date = input.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
        var body = new InvoiceInput {
            Kind = InvoiceKind.Purchase, BranchId = first.BranchId, CurrentAccountId = first.SupplierId, Date = date, Currency = first.Currency,
            ExternalNumber = input.ExternalNumber.Trim(), AffectsStock = false, Lines = lines,
            Notes = "Mal kabul: " + string.Join(", ", receipts.Select(r => r.Number))
        };
        var built   = await builder.BuildAsync(body, null, false, ct);
        var draft   = new Invoice { Status = InvoiceStatus.Draft, SourceType = ReceiptInvoiceLinks.Source, SourceId = first.Id };
        InvoiceBuilder.Apply(draft, body, built);
        for (var i = 0; i < built.Lines.Count; i++) {
            var line = built.Lines[i];
            line.InvoiceId    = draft.Id;
            line.SourceLineId = sources[i];
            draft.Lines.Add(line);
        }

        foreach (var due in built.Due) {
            due.InvoiceId = draft.Id;
            draft.DueSchedule.Add(due);
        }

        db.Invoices.Add(draft);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (InvoicingErrors.IsUniqueViolation(ex)) {
            db.ClearTracking();
            throw InvoiceErrors.ExternalNumberTaken();
        }

        return InvoiceRules.ToDto(draft, built.Cari.Name);
    }
}
