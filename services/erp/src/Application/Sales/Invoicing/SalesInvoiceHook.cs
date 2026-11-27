using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory.Tracking;
using ERP.Application.Invoicing.Posting;
using ERP.Application.Sales.Returns;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Invoicing;

/// <summary>
///     Satış faturalarının P4 kesim/iptal akışına bağlandığı yer (<see cref="IInvoicePostHook" />). Sevkten kesilen faturalarda: kesimde sipariş kilidi alınır,
///     faturalanan miktar yeniden denetlenir, vade planı siparişin taksit planından tüketilir (tüketim kaydedilir); sonra sevk/sipariş satırları faturalanmış
///     işaretlenir ve <see cref="InvoiceState" /> güncellenir. İptalde hepsi geri alınır. İade faturalarında (kaynak "sales-return") iptal, iade belgesinin
///     stok girişini geri alır. Kilit sırası: cari (çağıran), sipariş (BeforePost), stok (poster).
/// </summary>
public sealed class SalesInvoiceHook(
    IApplicationDbContext db,
    CurrencyPosting       currency,
    ReturnService         returns
) : IInvoicePostHook {
    private sealed record Context(Guid OrderId, List<Shipment> Shipments);

    private async Task<Context?> LoadAsync(Invoice invoice, bool track, CancellationToken ct) {
        if (!ShipmentInvoiceLinks.IsLinked(invoice)) return null;
        var lineIds = invoice.Lines.Where(l => l.SourceLineId != null).Select(l => l.SourceLineId!.Value).ToList();
        if (lineIds.Count == 0) return null;

        var shipmentIds = await db.ShipmentLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).Select(l => l.ShipmentId).Distinct().ToListAsync(ct);
        var query       = track ? db.Shipments.Include(s => s.Lines) : db.Shipments.AsNoTracking().Include(s => s.Lines);
        var shipments   = await query.Where(s => shipmentIds.Contains(s.Id)).ToListAsync(ct);
        return shipments.Count == 0 ? null : new Context(shipments[0].SalesOrderId, shipments);
    }

    public async Task BeforePostAsync(Invoice invoice, bool cancelling, CancellationToken ct) {
        if (invoice.Kind == InvoiceKind.SalesReturn && invoice.SourceType == ReturnService.Source) {
            if (cancelling && await returns.OrderIdAsync(invoice.SourceId, ct) is { } returnOrder) await db.LockSalesOrderAsync(returnOrder, ct);
            return;
        }

        var ctx = await LoadAsync(invoice, false, ct);
        if (ctx is null) {
            // Sevkten kesilen fatura bağsız kesilemez: faturalanan miktar ve taksit planı bağ olmadan izlenemez.
            if (!cancelling && ShipmentInvoiceLinks.IsLinked(invoice))
                throw SalesErrors.Conflict("sales_invoice_unlinked", "Sevkten kesilen faturanın satırları sevk satırlarına bağlı olmalıdır.");
            return;
        }

        if (!await db.LockSalesOrderAsync(ctx.OrderId, ct)) throw new NotFoundException(ctx.OrderId.ToString(), "SalesOrder");
        if (cancelling) return;
        if (invoice.Lines.Any(l => l.SourceLineId is null))
            throw SalesErrors.Conflict("sales_invoice_unlinked", "Sevkten kesilen faturanın satırları sevk satırlarına bağlı olmalıdır.");

        if (ctx.Shipments.Any(s => s.Status == ShipmentStatus.Cancelled))
            throw SalesErrors.Conflict("shipment_cancelled", "İptal edilmiş sevk faturalanamaz.");

        // Kilit altında yeniden denetim: bu fatura dahil iptal edilmemiş faturaların toplamı sevk miktarını aşamaz.
        await ShipmentInvoiceLinks.EnsureNotOverInvoicedAsync(db, invoice.Id,
                                                              invoice.Lines.Where(l => l.SourceLineId != null).Select(l => (l.SourceLineId!.Value, l.Quantity * l.Factor)), ct);

        await ConsumePlanAsync(invoice, ctx.OrderId, ct);
    }

    private async Task ConsumePlanAsync(Invoice invoice, Guid orderId, CancellationToken ct) {
        var plan = await db.InstallmentPlans.Include(p => p.Rows).FirstOrDefaultAsync(p => p.SalesOrderId == orderId, ct);
        if (plan is null) return;

        var order        = await db.SalesOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => new { o.Date, o.CurrentAccountId }).FirstAsync(ct);
        var cariCurrency = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == invoice.CurrentAccountId).Select(c => c.Currency).FirstAsync(ct);
        var posted       = await currency.ConvertAsync(invoice, cariCurrency, ct);
        if (PlanScheduling.Plan(plan, posted.Amount, order.Date) is not { } outcome) return;

        // Vade satırları planın sırasıyla yeniden yazılır (taslaktaki önizleme eskimiş olabilir).
        db.InvoiceDueLines.RemoveRange(invoice.DueSchedule);
        invoice.DueSchedule.Clear();
        await db.SaveChangesAsync(ct);
        var sort = 1;
        foreach (var row in outcome.Schedule.OrderBy(r => r.DueDate).ThenBy(r => r.Sequence))
            invoice.DueSchedule.Add(new InvoiceDueLine { InvoiceId = invoice.Id, Sort = sort++, DueDate = row.DueDate, Amount = row.Amount });

        foreach (var (rowId, amount) in outcome.Consumed) {
            if (rowId is { } id) plan.Rows.First(r => r.Id == id).Remaining -= amount;
            else plan.DownPaymentRemaining                                   -= amount;
            db.InvoicePlanConsumptions.Add(new InvoicePlanConsumption { InvoiceId = invoice.Id, SalesOrderId = orderId, PlanRowId = rowId, Amount = amount });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid WarehouseId, Guid ProductId, Guid? VariantId)>> StockKeysAsync(Invoice invoice, bool cancelling, CancellationToken ct) {
        if (!cancelling || invoice.Kind != InvoiceKind.SalesReturn || invoice.SourceType != ReturnService.Source) return [];
        var keys = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == ReturnService.StockSource && m.SourceId == invoice.SourceId && m.Quantity != 0m)
                           .Select(m => new { m.WarehouseId, m.ProductId, m.VariantId, Tracked = m.LotLines.Any() }).Distinct().ToListAsync(ct);
        return keys.Select(k => (k.WarehouseId, k.ProductId, k.VariantId)).Concat(keys.Where(k => k.Tracked).Select(k => TrackingValidator.ItemKey(k.ProductId))).ToList();
    }

    public async Task OnIssuedAsync(Invoice invoice, CancellationToken ct) {
        var ctx = await LoadAsync(invoice, true, ct);
        if (ctx is null) return;

        var order = await db.SalesOrders.Include(o => o.Lines).FirstAsync(o => o.Id == ctx.OrderId, ct);
        foreach (var line in invoice.Lines.Where(l => l.SourceLineId != null)) {
            var shipment = ctx.Shipments.FirstOrDefault(s => s.Lines.Any(l => l.Id == line.SourceLineId));
            var sl       = shipment?.Lines.First(l => l.Id == line.SourceLineId);
            if (shipment is null || sl is null) continue;

            var quantity = Math.Round(line.Quantity * line.Factor / sl.Factor, 6, MidpointRounding.AwayFromZero);
            sl.InvoicedQuantity += quantity;
            order.Lines.First(l => l.Id == sl.SalesOrderLineId).InvoicedQuantity += quantity;
            shipment.InvoiceId = invoice.Id;
        }

        order.InvoiceState = State(order);
        await db.SaveChangesAsync(ct);
    }

    public async Task OnCancelledAsync(Invoice invoice, CancellationToken ct) {
        if (invoice.Kind == InvoiceKind.SalesReturn && invoice.SourceType == ReturnService.Source) {
            await returns.OnInvoiceCancelledAsync(invoice, ct);
            return;
        }

        var ctx = await LoadAsync(invoice, true, ct);
        if (ctx is null) return;

        var order = await db.SalesOrders.Include(o => o.Lines).FirstAsync(o => o.Id == ctx.OrderId, ct);
        foreach (var line in invoice.Lines.Where(l => l.SourceLineId != null)) {
            var shipment = ctx.Shipments.FirstOrDefault(s => s.Lines.Any(l => l.Id == line.SourceLineId));
            var sl       = shipment?.Lines.First(l => l.Id == line.SourceLineId);
            if (shipment is null || sl is null) continue;

            var quantity = Math.Round(line.Quantity * line.Factor / sl.Factor, 6, MidpointRounding.AwayFromZero);
            sl.InvoicedQuantity = Math.Max(0m, sl.InvoicedQuantity - quantity);
            var orderLine = order.Lines.First(l => l.Id == sl.SalesOrderLineId);
            orderLine.InvoicedQuantity = Math.Max(0m, orderLine.InvoicedQuantity - quantity);
        }

        foreach (var shipment in ctx.Shipments.Where(s => s.InvoiceId == invoice.Id)) {
            var lineIds = shipment.Lines.Select(l => l.Id).ToList();
            shipment.InvoiceId = await db.InvoiceLines.AsNoTracking().Where(l => l.SourceLineId != null && lineIds.Contains(l.SourceLineId.Value))
                                         .Join(db.Invoices.Where(i => i.Status == InvoiceStatus.Issued && i.Id != invoice.Id && i.SourceType == ShipmentInvoiceLinks.Source),
                                               l => l.InvoiceId, i => i.Id, (l, i) => new { i.Id, i.Date, i.Created }).OrderByDescending(i => i.Date).ThenByDescending(i => i.Created)
                                         .Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        }

        // Taksit planından tüketilen tutarlar geri verilir.
        var consumed = await db.InvoicePlanConsumptions.Where(c => c.InvoiceId == invoice.Id).ToListAsync(ct);
        if (consumed.Count > 0) {
            var plan = await db.InstallmentPlans.Include(p => p.Rows).FirstOrDefaultAsync(p => p.SalesOrderId == ctx.OrderId, ct);
            if (plan is not null)
                foreach (var c in consumed) {
                    if (c.PlanRowId is { } id) {
                        if (plan.Rows.FirstOrDefault(r => r.Id == id) is { } row) row.Remaining = Math.Min(row.Amount, row.Remaining + c.Amount);
                    } else {
                        plan.DownPaymentRemaining = Math.Min(plan.DownPayment, plan.DownPaymentRemaining + c.Amount);
                    }
                }

            db.InvoicePlanConsumptions.RemoveRange(consumed);
        }

        order.InvoiceState = State(order);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Faturalama durumu: tüm sipariş miktarı faturalandıysa Full, bir kısmı faturalandıysa Partial.</summary>
    public static InvoiceState State(SalesOrder order) {
        var counted = order.Lines.Where(l => l.Quantity > 0m).ToList();
        if (counted.Count > 0 && counted.All(l => l.InvoicedQuantity >= l.Quantity)) return InvoiceState.Full;
        return counted.Any(l => l.InvoicedQuantity > 0m) ? InvoiceState.Partial : InvoiceState.None;
    }
}
