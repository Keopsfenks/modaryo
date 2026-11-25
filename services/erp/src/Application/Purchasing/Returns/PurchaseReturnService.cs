using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Inventory.Tracking;
using ERP.Application.Invoicing;
using ERP.Application.Purchasing.Receipts;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Returns;

public sealed record ReturnLineRequest(Guid ReceiptLineId, decimal Quantity, IReadOnlyList<LotInput>? Lots, IReadOnlyList<string>? Serials);

public sealed record ReturnOutcome(Guid ReturnId, Guid? InvoiceId, string? InvoiceNumber);

/// <summary>
///     Alış iadesi: stok çıkışı (PurchaseReturn) ve istenirse alış iade faturası taslağı (AffectsStock=false: stok burada düşer).
///     Çağıran açık işlemde, stok kilitleri alınmış olarak çağırır ve işlemi kendisi onaylar. Kilit anahtarları
///     <see cref="KeysFor" /> ile önceden alınır; miktar denetimleri kilitten SONRA burada yapılır.
/// </summary>
public sealed class PurchaseReturnService(
    IApplicationDbContext db,
    IStockService         stock,
    ReceiptLineStock      lineStock,
    InvoiceBuilder        invoices,
    IOutbox               outbox
) {
    /// <summary>Satırların olası kaynak depoları (karantina ve hedef) x kalem anahtarları; takipli kalemlerde kalem kilidi dahil.</summary>
    public static IEnumerable<(Guid, Guid, Guid?)> KeysFor(IEnumerable<GoodsReceiptLine> lines, IReadOnlyDictionary<Guid, Product> products) {
        foreach (var line in lines) {
            yield return (line.WarehouseId, line.ProductId, line.VariantId);
            if (line.TargetWarehouseId is { } target) yield return (target, line.ProductId, line.VariantId);
            if (products.TryGetValue(line.ProductId, out var product) && TrackingValidator.IsTracked(product)) yield return TrackingValidator.ItemKey(line.ProductId);
        }
    }

    public async Task<ReturnOutcome> ReturnAsync(GoodsReceipt receipt, IReadOnlyList<ReturnLineRequest> requests, DateOnly date, string reason, bool createInvoice,
                                                 bool quarantineOnly, CancellationToken ct, bool invoiceOptional = false) {
        if (receipt.Status != GoodsReceiptStatus.Posted) throw PurchasingErrors.Conflict("receipt_cancelled", "İptal edilmiş mal kabul için iade yapılamaz.");
        if (requests.GroupBy(r => r.ReceiptLineId).Any(g => g.Count() > 1))
            throw PurchasingErrors.Bad("return_line_duplicate", "Aynı mal kabul satırı bir iadede yalnızca bir kez verilebilir.");

        var returnId   = Guid.CreateVersion7();
        var productIds = receipt.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var stockLines = new List<StockLine>();
        var planned    = new List<(GoodsReceiptLine Line, decimal Base)>();
        foreach (var request in requests) {
            var line = receipt.Lines.FirstOrDefault(l => l.Id == request.ReceiptLineId)
                       ?? throw PurchasingErrors.Bad("return_line_invalid", "İade satırı bu mal kabule ait değil.");
            var product = products[line.ProductId];
            var baseQty = Math.Round(request.Quantity * ReceiptLineStock.Factor(line), 6, MidpointRounding.AwayFromZero);
            if (baseQty <= 0m) throw PurchasingErrors.Bad("invalid_quantity", "İade miktarı sıfırdan büyük olmalıdır.");
            if (await lineStock.ReturnedAsync(line.Id, ct) + baseQty > line.BaseQuantity)
                throw PurchasingErrors.Bad("return_exceeds_received", $"\"{product.Code} - {product.Name}\" için iade miktarı gelen miktarı aşıyor.");

            // Kaynak: önce karantinada bekleyen kısım, kalanı hedef (normal) depodan.
            var held = await lineStock.OpenAsync(line, ct);
            if (quarantineOnly && baseQty > held) throw PurchasingErrors.Conflict("quarantine_exceeds_open", "İade miktarı karantinadaki açık miktarı aşıyor.");
            var fromQ    = Math.Min(baseQty, held);
            var rest     = baseQty - fromQ;
            var restFrom = line.Quarantine == QuarantineState.None ? line.WarehouseId : line.TargetWarehouseId ?? line.WarehouseId;

            var tracked = TrackingValidator.IsTracked(product);
            if (tracked && fromQ > 0m && rest > 0m)
                throw PurchasingErrors.Bad("return_split_tracked", "Takipli kalemde iade tek depodan yapılmalıdır; karantina ve depo miktarlarını ayrı iade edin.");

            if (fromQ > 0m) {
                var derive = tracked && request.Lots is null && request.Serials is null;
                var (lots, serials) = derive ? await lineStock.DeriveAsync(line, product, fromQ, ct) : (request.Lots, request.Serials);
                stockLines.Add(new StockLine(line.WarehouseId, line.ProductId, line.VariantId, fromQ, null, lots, serials, line.Id));
            }

            if (rest > 0m) stockLines.Add(new StockLine(restFrom, line.ProductId, line.VariantId, rest, null, request.Lots, request.Serials, line.Id));
            planned.Add((line, baseQty));
        }

        await stock.IssueAsync(MovementType.PurchaseReturn, ReceiptLineStock.ReturnSource, returnId, date, stockLines, ct);
        foreach (var (line, _) in planned) await lineStock.CloseIfResolvedAsync(line, ct);

        Guid?   invoiceId     = null;
        string? invoiceNumber = null;
        if (createInvoice) (invoiceId, invoiceNumber) = await CreateInvoiceDraftAsync(receipt, planned, date, returnId, invoiceOptional, ct);

        outbox.Enqueue("purchase.returned", new {
            poId = receipt.PurchaseOrderId, receiptId = receipt.Id, returnId, reason, invoiceId,
            lines = planned.Select(p => new { receiptLineId = p.Line.Id, productId = p.Line.ProductId, variantId = p.Line.VariantId, baseQuantity = p.Base }).ToList()
        });
        await db.SaveChangesAsync(ct);
        return new ReturnOutcome(returnId, invoiceId, invoiceNumber);
    }

    /// <summary>Alış iade faturası taslağı: mal kabulün kesilmiş alış faturasına (kaynak "purchase-receipt") bağlanır; satır fiyatları asıl faturadan gelir.</summary>
    private async Task<(Guid?, string?)> CreateInvoiceDraftAsync(GoodsReceipt receipt, List<(GoodsReceiptLine Line, decimal Base)> planned, DateOnly date, Guid returnId,
                                                                 bool optional, CancellationToken ct) {
        var original = (await ERP.Application.Purchasing.Invoices.ReceiptInvoiceLinks.InvoicesAsync(db, [receipt.Id], ct))
                       .Where(i => i.Status == InvoiceStatus.Issued).OrderByDescending(i => i.Date).FirstOrDefault();
        if (original is null) {
            if (optional) return (null, null);
            throw PurchasingErrors.Conflict("purchase_invoice_required", "İade faturası için mal kabule bağlı kesilmiş bir alış faturası gerekir.");
        }

        var lines = new List<ERP.Application.Invoicing.LineInput>();
        foreach (var (line, baseQty) in planned) {
            var source = original.Lines.FirstOrDefault(l => l.SourceLineId == line.Id)
                         ?? original.Lines.FirstOrDefault(l => l.ProductId == line.ProductId && l.VariantId == line.VariantId)
                         ?? throw PurchasingErrors.Conflict("purchase_invoice_required", "Alış faturasında iade edilen kalem bulunamadı.");
            lines.Add(new ERP.Application.Invoicing.LineInput {
                ProductId = source.ProductId, VariantId = source.VariantId, Description = source.Description, Unit = source.Unit,
                Quantity = Math.Round(baseQty / source.Factor, 6, MidpointRounding.AwayFromZero), UnitPrice = source.UnitPrice, VatIncluded = source.VatIncluded,
                DiscountPercent = source.DiscountPercent, VatRate = source.VatRate, SourceLineId = source.Id
            });
        }

        var input = new InvoiceInput {
            Kind = InvoiceKind.PurchaseReturn, BranchId = receipt.BranchId, CurrentAccountId = receipt.SupplierId, Date = date, Currency = original.Currency,
            Rate = original.Currency == "TRY" ? null : original.Rate, ExternalNumber = "IADE-" + receipt.Number + "-" + returnId.ToString("N")[..6], AffectsStock = false,
            ReturnOfInvoiceId = original.Id, Notes = "Mal kabul " + receipt.Number + " iadesi", Lines = lines
        };
        var built   = await invoices.BuildAsync(input, null, false, ct, systemGenerated: true);
        var invoice = new Invoice { Status = InvoiceStatus.Draft, SourceType = "purchase-return", SourceId = returnId };
        InvoiceBuilder.Apply(invoice, input, built);
        foreach (var l in built.Lines) {
            l.InvoiceId = invoice.Id;
            invoice.Lines.Add(l);
        }

        foreach (var due in built.Due) {
            due.InvoiceId = invoice.Id;
            invoice.DueSchedule.Add(due);
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return (invoice.Id, invoice.Number);
    }
}
