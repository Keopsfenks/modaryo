using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Invoicing;

/// <summary>Taksit planından bir faturanın tükettiği tutarlar: vade satırları ve plan satırı başına tüketim (PlanRowId null = peşinat).</summary>
public sealed record PlanOutcome(IReadOnlyList<PlanRow> Schedule, IReadOnlyList<(Guid? RowId, decimal Amount)> Consumed);

public static class PlanScheduling {
    /// <summary>
    ///     Plan sırayla tüketilir (önce kalan peşinat, sonra satırlar; son satır gerekirse bölünür). Fatura kalan plan kapasitesini aşarsa
    ///     (kur ya da yuvarlama farkı) fazlası son vade satırına eklenir. Plan yoksa ya da tükenmişse null döner.
    /// </summary>
    public static PlanOutcome? Plan(InstallmentPlan? plan, decimal payable, DateOnly orderDate) {
        if (plan is null || payable <= 0m) return null;

        var rows     = plan.Rows.Where(r => r.Remaining > 0m).OrderBy(r => r.Sequence).Select(r => new PlanRow(r.Sequence, r.DueDate, r.Remaining)).ToList();
        var capacity = plan.DownPaymentRemaining + rows.Sum(r => r.Amount);
        if (capacity <= 0m) return null;

        var take   = Math.Min(decimal.Round(payable, 2), capacity);
        var excess = decimal.Round(payable, 2) - take;
        var (schedule, remaining, downLeft) = PlanConsumer.Consume(rows, plan.DownPaymentRemaining, take, orderDate);

        var consumed = new List<(Guid?, decimal)>();
        if (plan.DownPaymentRemaining - downLeft is > 0m and var down) consumed.Add((null, down));
        foreach (var row in plan.Rows.Where(r => r.Remaining > 0m)) {
            var left = remaining.FirstOrDefault(r => r.Sequence == row.Sequence)?.Amount ?? 0m;
            if (row.Remaining - left > 0m) consumed.Add((row.Id, row.Remaining - left));
        }

        var list = schedule.ToList();
        if (excess > 0m && list.Count > 0) list[^1] = list[^1] with { Amount = list[^1].Amount + excess };
        return new PlanOutcome(list, consumed);
    }
}

/// <summary>
///     Sevklerden satış faturası taslağı. Satırlar sevk satırlarının henüz faturalanmamış miktarlarıdır (iptal edilmemiş faturalar, taslaklar dahil, düşülür);
///     fiyat, iskonto ve KDV sipariş satırından donmuş gelir (liste fiyatı sonradan değişse de aynı kalır). Taslak stok etkilemez (stok sevkte çıkmıştır).
///     Vade planı sipariş taksit planından önizlenir; gerçek tüketim kesimde, sipariş kilidi altında yapılır. Çağıran açık işlemde ve sipariş kilidi
///     alınmış olarak çağırır.
/// </summary>
public sealed class ShipmentInvoiceBuilder(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    InvoiceBuilder        builder,
    TimeProvider          clock
) {
    public async Task<(Invoice Draft, string CariName)> CreateDraftAsync(IReadOnlyList<Guid> shipmentIds, DateOnly? invoiceDate, CancellationToken ct) {
        var ids       = shipmentIds.Distinct().ToList();
        var shipments = await db.Shipments.AsNoTracking().Include(s => s.Lines).Where(s => ids.Contains(s.Id)).OrderBy(s => s.Date).ThenBy(s => s.Number).ToListAsync(ct);
        if (shipments.Count != ids.Count) throw new NotFoundException(string.Join(",", ids.Except(shipments.Select(s => s.Id))), "Shipment");
        foreach (var s in shipments) BranchGuard.Ensure(tenant, s.BranchId);

        var first = shipments[0];
        if (shipments.Any(s => s.SalesOrderId != first.SalesOrderId)) throw SalesErrors.Bad("order_mismatch", "Faturalanacak sevklerin siparişi aynı olmalıdır.");
        ShipmentInvoiceLinks.EnsureInvoiceable(shipments);

        var order = await db.SalesOrders.AsNoTracking().Include(o => o.Lines).Include(o => o.Plan).ThenInclude(p => p!.Rows).FirstAsync(o => o.Id == first.SalesOrderId, ct);
        var lineIds  = shipments.SelectMany(s => s.Lines).Select(l => l.Id).ToList();
        var invoiced = await ShipmentInvoiceLinks.InvoicedBaseAsync(db, lineIds, null, ct);

        var lines   = new List<LineInput>();
        var sources = new List<Guid>();
        foreach (var shipment in shipments) {
            foreach (var line in shipment.Lines.OrderBy(l => l.Sort)) {
                var shippedBase = line.Quantity * line.Factor;
                var openBase    = shippedBase - invoiced.GetValueOrDefault(line.Id);
                if (openBase <= 0m || shippedBase <= 0m) continue;

                var orderLine = order.Lines.First(l => l.Id == line.SalesOrderLineId);
                var open      = Math.Round(openBase / line.Factor, 6, MidpointRounding.AwayFromZero);
                var (width, length) = Dimensions(line, open);
                lines.Add(new LineInput {
                    ProductId = line.ProductId, VariantId = line.VariantId, Description = line.Description, Unit = line.Unit, Quantity = open, Width = width, Length = length,
                    UnitPrice = orderLine.UnitPrice, VatIncluded = orderLine.VatIncluded, DiscountPercent = orderLine.DiscountPercent, VatRate = orderLine.VatRate,
                    Lots = line.Lots?.ToList(), Serials = line.Serials?.ToList()
                });
                sources.Add(line.Id);
            }
        }

        if (lines.Count == 0) throw SalesErrors.Conflict("nothing_to_invoice", "Seçilen sevkler zaten faturalanmış.");

        var date = invoiceDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
        var body = new InvoiceInput {
            Kind = InvoiceKind.Sales, BranchId = order.BranchId, CurrentAccountId = order.CurrentAccountId, Date = date, Currency = order.Currency,
            DocDiscountPercent = order.DocDiscount > 0m ? order.DocDiscount : null, AffectsStock = false,
            Notes = $"Sipariş: {order.Number}; sevk: {string.Join(", ", shipments.Select(s => s.Number))}", Lines = lines
        };
        var built = await builder.BuildAsync(body, null, false, ct);

        // Vade önizlemesi: sipariş planından (kesimde sipariş kilidi altında yeniden hesaplanır).
        if (PlanScheduling.Plan(order.Plan, built.LedgerAmount, order.Date) is { } outcome) {
            var sort = 1;
            foreach (var row in outcome.Schedule.OrderBy(r => r.DueDate).ThenBy(r => r.Sequence)) built.Due.Add(new InvoiceDueLine { Sort = sort++, DueDate = row.DueDate, Amount = row.Amount });
        }

        var draft = new Invoice { Status = InvoiceStatus.Draft, SourceType = ShipmentInvoiceLinks.Source, SourceId = first.Id };
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
        await db.SaveChangesAsync(ct);
        return (draft, built.Cari.Name);
    }

    /// <summary>
    ///     Ölçüyle satılan kalemlerde fatura miktarı en x boydur. Tam miktarda sevkin ölçüleri kullanılır; kısmi miktarda boy,
    ///     miktar / en olarak türetilir (4 ondalıkta tam çıkmıyorsa kısmi fatura mümkün değildir).
    /// </summary>
    private static (decimal? Width, decimal? Length) Dimensions(ShipmentLine line, decimal open) {
        if (line.Length is not { } length) return (null, null);
        var full = line.Width is { } w ? Math.Round(w * length, 6, MidpointRounding.AwayFromZero) : length;
        if (open == full) return (line.Width, length);

        if (line.Width is { } width and > 0m) {
            var derived = Math.Round(open / width, 4, MidpointRounding.AwayFromZero);
            if (Math.Round(width * derived, 6, MidpointRounding.AwayFromZero) == open) return (width, derived);
        } else if (Math.Round(open, 4, MidpointRounding.AwayFromZero) == open) {
            return (null, open);
        }

        throw SalesErrors.Bad("measure_partial_invalid", $"\"{line.Description}\" için kısmi miktar ölçülere bölünemiyor; tam miktarı faturalayın.");
    }
}
