using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Invoicing;
using ERP.Domain.Purchasing;
using ERP.Domain.Sales;
using ERP.Domain.Treasury;

namespace ERP.Application.Sales.Orders;

public static class SalesStock {
    /// <summary>Satış siparişi rezervasyonlarının kaynak türü (sevkiyat ve iptal aynı değeri kullanır).</summary>
    public const string SourceType = "sales-order";
}

public static class OrderRules {
    public static async Task<SalesOrder> LoadAsync(IApplicationDbContext db, ICurrentTenant tenant, Guid id, bool track, CancellationToken ct) {
        var query = db.SalesOrders.Include(o => o.Lines).Include(o => o.Plan).ThenInclude(p => p!.Rows).AsQueryable();
        if (!track) query = query.AsNoTracking();
        var order = await query.FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException(id.ToString(), "SalesOrder");
        BranchGuard.Ensure(tenant, order.BranchId);
        return order;
    }

    /// <summary>Faturalanmamış tutarı cari riskini oluşturan sipariş durumları.</summary>
    public static readonly SalesOrderStatus[] OpenStatuses = [SalesOrderStatus.AwaitingPayment, SalesOrderStatus.Approved, SalesOrderStatus.PartiallyShipped, SalesOrderStatus.Shipped];

    /// <summary>Gereken ön ödeme (sipariş para biriminde).</summary>
    public static decimal PrepaymentRequired(SalesOrder order) {
        if (!order.RequiresPrepayment) return 0m;
        if (order.PrepaymentAmount is { } amount) return decimal.Round(Math.Min(amount, order.Total), 2);
        return decimal.Round(order.Total * (order.PrepaymentPercent ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Sipariş para biriminden cari para birimine çevirir (TRY üzerinden, satış kuru).</summary>
public sealed class SalesCurrency(IExchangeRates rates) {
    public async Task<decimal> ConvertAsync(decimal amount, string from, string to, DateOnly date, CancellationToken ct) {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) || amount == 0m) return amount;
        var tl = amount;
        if (!string.Equals(from, "TRY", StringComparison.OrdinalIgnoreCase)) tl = amount * (await rates.GetAsync(from, date, RateType.ForexSelling, ct)).Rate;
        if (!string.Equals(to, "TRY", StringComparison.OrdinalIgnoreCase)) tl /= (await rates.GetAsync(to, date, RateType.ForexSelling, ct)).Rate;
        return decimal.Round(tl, 2, MidpointRounding.AwayFromZero);
    }
}

public sealed class OrderInputValidator : AbstractValidator<OrderInput> {
    public OrderInputValidator() {
        RuleFor(i => i).Custom((i, ctx) => {
            var result = new QuoteInputValidator().Validate(i.ToQuoteInput());
            foreach (var e in result.Errors) ctx.AddFailure(e.PropertyName, e.ErrorMessage);
        });
        RuleFor(i => i.PrepaymentPercent).InclusiveBetween(0m, 100m).When(i => i.PrepaymentPercent is not null).WithMessage("Ön ödeme yüzdesi 0 ile 100 arasında olmalıdır.");
        RuleFor(i => i.PrepaymentAmount).GreaterThanOrEqualTo(0m).When(i => i.PrepaymentAmount is not null).WithMessage("Ön ödeme tutarı negatif olamaz.");
    }
}

/// <summary>Girdiyi doğrulayıp sipariş başlığına ve satırlarına yazar.</summary>
public sealed class OrderWriter(
    SalesLineBuilder      builder,
    IApplicationDbContext db
) {
    public async Task ApplyAsync(SalesOrder order, OrderInput input, CancellationToken ct) {
        await builder.EnsureBranchAsync(input.BranchId, ct);
        var customer = await builder.LoadCustomerAsync(input.CurrentAccountId, ct);
        var (repId, repName) = await builder.ResolveRepAsync(input.SalesRepUserId, customer, ct);
        var currency = input.Currency.Trim().ToUpperInvariant();
        var document = await builder.BuildAsync(customer, currency, input.Date, input.DocDiscount, input.Lines.Select(l => l.ToQuoteLine()).ToList(), ct);

        var warehouseIds = input.Lines.Where(l => l.WarehouseId is not null).Select(l => l.WarehouseId!.Value).Distinct().ToList();
        if (warehouseIds.Count > 0) {
            var found = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).Select(w => new { w.Id, w.BranchId }).ToListAsync(ct);
            foreach (var id in warehouseIds) {
                var w = found.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException(id.ToString(), "Warehouse");
                if (w.BranchId != input.BranchId) throw SalesErrors.Bad("warehouse_branch_mismatch", "Depo, siparişin şubesine ait değil.");
            }
        }

        var requires = input.RequiresPrepayment ?? false;
        if (requires && input.PrepaymentPercent is null && input.PrepaymentAmount is null)
            throw SalesErrors.Bad("prepayment_required", "Ön ödeme için yüzde ya da tutar girilmelidir.");
        if (requires && input.PrepaymentAmount is { } pa && pa > document.Calc.Payable)
            throw SalesErrors.Bad("prepayment_exceeds_total", "Ön ödeme tutarı sipariş toplamını aşamaz.");

        order.CurrentAccountId   = customer.Id;
        order.BranchId           = input.BranchId;
        order.Date               = input.Date;
        order.Currency           = currency;
        order.SalesRepUserId     = repId;
        order.SalesRepName       = repName;
        order.DocDiscount        = input.DocDiscount ?? 0m;
        order.Notes              = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        order.RequiresPrepayment = requires;
        order.PrepaymentPercent  = requires ? input.PrepaymentPercent : null;
        order.PrepaymentAmount   = requires ? input.PrepaymentAmount : null;
        order.Gross              = document.Calc.Gross;
        order.Discount           = document.Calc.Discount;
        order.Net                = document.Calc.Net;
        order.Vat                = document.Calc.Vat;
        order.Total              = document.Calc.Payable;

        for (var i = 0; i < document.Lines.Count; i++) {
            var l = document.Lines[i];
            order.Lines.Add(new SalesOrderLine {
                SalesOrderId = order.Id, Sort = l.Sort, ProductId = l.ProductId, VariantId = l.VariantId, Description = l.Description, Unit = l.Unit, Factor = l.Factor,
                Quantity = l.Quantity, Width = l.Width, Length = l.Length, UnitPrice = l.UnitPrice, VatIncluded = l.VatIncluded, DiscountPercent = l.DiscountPercent,
                VatRate = l.VatRate, SupplyMode = l.SupplyMode, SupplierId = l.SupplierId, WarehouseId = input.Lines[i].WarehouseId, Net = l.Net, Vat = l.Vat, Total = l.Total
            });
        }
    }
}

public sealed class OrderMapper(
    IApplicationDbContext db,
    ICurrentSession       session,
    SalesCurrency         currency
) {
    public async Task<OrderDto> ToDtoAsync(SalesOrder order, IReadOnlyList<string>? warnings, CancellationToken ct) {
        var customer = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == order.CurrentAccountId).Select(c => new { c.Name, c.Currency }).FirstOrDefaultAsync(ct);
        var ids      = order.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);

        var showCost = session.HasPermission(SalesPermissions.Cost);
        var costs    = showCost ? await UnitCostsAsync(order, ct) : new Dictionary<Guid, decimal>();

        var poIds = order.Lines.Where(l => l.PurchaseOrderId is not null).Select(l => l.PurchaseOrderId!.Value).Distinct().ToList();
        var pos   = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).Where(p => poIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var receipts = poIds.Count == 0
            ? new List<GoodsReceipt>()
            : await db.GoodsReceipts.AsNoTracking().Where(r => r.PurchaseOrderId != null && poIds.Contains(r.PurchaseOrderId.Value) && r.Status == GoodsReceiptStatus.Posted)
                      .OrderBy(r => r.Date).ThenBy(r => r.Number).ToListAsync(ct);

        decimal totalCost = 0m, totalNet = 0m;
        var     lines     = new List<OrderLineDto>();
        foreach (var l in order.Lines.OrderBy(l => l.Sort)) {
            var      info = products.GetValueOrDefault(l.ProductId);
            decimal? unitCost = null, margin = null;
            if (showCost) {
                if (costs.TryGetValue(l.Id, out var perBase)) {
                    unitCost = decimal.Round(perBase * l.Factor, 6, MidpointRounding.AwayFromZero);
                    var unitNet = l.Quantity == 0m ? 0m : l.Net / l.Quantity;
                    margin     = unitNet == 0m ? null : decimal.Round((unitNet - unitCost.Value) / unitNet * 100m, 2, MidpointRounding.AwayFromZero);
                    totalCost += unitCost.Value * l.Quantity;
                }

                totalNet += l.Net;
            }

            pos.TryGetValue(l.PurchaseOrderId ?? Guid.Empty, out var po);
            lines.Add(new OrderLineDto(l.Id, l.Sort, l.ProductId, info?.Code ?? string.Empty, info?.Name ?? l.Description, l.VariantId, l.Description, l.Unit, l.Factor, l.Quantity,
                                       l.Width, l.Length, l.UnitPrice, l.VatIncluded, l.DiscountPercent, l.VatRate, l.SupplyMode, l.WarehouseId, l.SupplierId, l.PurchaseOrderId,
                                       po?.Number, l.Net, l.Vat, l.Total, l.ShippedQuantity, l.InvoicedQuantity, l.ReturnedQuantity, l.OpenQuantity, unitCost, margin,
                                       Timeline(order, l, po, receipts)));
        }

        var reservations = (await db.Reservations.AsNoTracking().Where(r => r.SourceType == SalesStock.SourceType && r.SourceId == order.Id).OrderBy(r => r.Id).ToListAsync(ct))
            .Select(r => new OrderReservationDto(r.SourceLineId, r.WarehouseId, r.Quantity, Camel(r.Status), r.ExpectedReceiptSourceId)).ToList();
        var shipments = (await db.Shipments.AsNoTracking().Where(s => s.SalesOrderId == order.Id).OrderBy(s => s.Date).ThenBy(s => s.Number).ToListAsync(ct))
            .Select(s => new OrderShipmentRefDto(s.Id, s.Number, Camel(s.Status), s.Date)).ToList();
        var shipmentLineIds = await db.ShipmentLines.AsNoTracking().Join(db.Shipments.Where(s => s.SalesOrderId == order.Id), l => l.ShipmentId, s => s.Id, (l, _) => l.Id).ToListAsync(ct);
        var invoiceIds = await db.InvoiceLines.AsNoTracking().Where(l => l.SourceLineId != null && shipmentLineIds.Contains(l.SourceLineId.Value))
                                 .Join(db.Invoices.Where(i => i.SourceType == "sales-shipment" && i.Status != ERP.Domain.Invoicing.InvoiceStatus.Cancelled), l => l.InvoiceId, i => i.Id, (l, i) => i.Id)
                                 .Distinct().ToListAsync(ct);
        var invoices = (await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).OrderBy(i => i.Date).ToListAsync(ct))
            .Select(i => new OrderInvoiceRefDto(i.Id, i.Number ?? string.Empty, Camel(i.Status), i.Date)).ToList();

        var plan = order.Plan is { } p
            ? new PlanDto(p.DownPayment, p.DownPaymentRemaining, p.InstallmentCount, p.FirstDueDate,
                          p.Rows.OrderBy(r => r.Sequence).Select(r => new PlanRowDto(r.Sequence, r.DueDate, r.Amount, r.Remaining)).ToList())
            : null;

        var (advances, advanceTotal) = await AdvancesAsync(order.Id, ct);
        var required = order.RequiresPrepayment && customer is not null
            ? await currency.ConvertAsync(OrderRules.PrepaymentRequired(order), order.Currency, customer.Currency, order.Date, ct)
            : 0m;

        return new OrderDto(order.Id, order.Number, order.Status, order.InvoiceState, order.CurrentAccountId, customer?.Name ?? string.Empty, order.BranchId, order.Currency,
                            order.SalesRepUserId, order.SalesRepName, order.QuoteId, order.Date, order.DocDiscount, order.Notes, order.CancelReason, order.RequiresPrepayment,
                            order.PrepaymentPercent, order.PrepaymentAmount, required, advanceTotal, order.Gross, order.Discount, order.Net, order.Vat, order.Total,
                            showCost ? decimal.Round(totalCost, 2) : null,
                            showCost && totalNet != 0m ? decimal.Round((totalNet - totalCost) / totalNet * 100m, 2, MidpointRounding.AwayFromZero) : null,
                            order.Version, lines, reservations, shipments, invoices, plan, advances, warnings ?? []);
    }

    /// <summary>Referans durumları da diğer tüm enum'lar gibi camelCase serileşir (JsonStringEnumConverter ile aynı biçim).</summary>
    private static string Camel(Enum value) => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    /// <summary>Cari para biriminde, siparişe bağlı iptal edilmemiş tahsilatlar (defter alacağı).</summary>
    public async Task<(IReadOnlyList<AdvanceDto> Items, decimal Total)> AdvancesAsync(Guid orderId, CancellationToken ct) {
        var docs = await db.FinanceDocuments.AsNoTracking()
                           .Where(d => d.SalesOrderId == orderId && d.Kind == FinanceDocumentKind.Collection && d.CancelledAt == null
                                       && db.SalesOrders.Any(o => o.Id == orderId && o.CurrentAccountId == d.CurrentAccountId))
                           .OrderBy(d => d.Date).ThenBy(d => d.Number).ToListAsync(ct);
        var docIds = docs.Select(d => d.Id).ToList();
        var credits = await db.LedgerEntries.AsNoTracking()
                              .Where(e => e.Source == LedgerEntrySource.Payment && e.SourceId != null && docIds.Contains(e.SourceId.Value) && e.CancelledAt == null)
                              .Select(e => e.Credit).ToListAsync(ct);
        return (docs.Select(d => new AdvanceDto(d.Id, d.Number, d.Date, d.Amount, d.Currency)).ToList(), credits.Sum());
    }

    private async Task<Dictionary<Guid, decimal>> UnitCostsAsync(SalesOrder order, CancellationToken ct) {
        var productIds = order.Lines.Select(l => l.ProductId).Distinct().ToList();
        var rows       = await db.AverageCosts.AsNoTracking().Where(c => productIds.Contains(c.ProductId)).ToListAsync(ct);
        var result     = new Dictionary<Guid, decimal>();
        foreach (var l in order.Lines) {
            var own  = rows.Where(c => c.ProductId == l.ProductId && c.VariantId == l.VariantId).ToList();
            var pick = (l.WarehouseId is { } w ? own.FirstOrDefault(c => c.WarehouseId == w) : null) ?? own.OrderByDescending(c => c.AverageCost).FirstOrDefault();
            if (pick is not null) result[l.Id] = pick.AverageCost;
        }

        return result;
    }

    /// <summary>Tedarik zaman çizgisi: Tedarikçiye sipariş verildi, Kargoda, Geldi, Müşteriye teslim edildi.</summary>
    private static IReadOnlyList<TimelineStepDto> Timeline(SalesOrder order, SalesOrderLine line, PurchaseOrder? po, List<GoodsReceipt> receipts) {
        if (line.SupplyMode != SupplyMode.BackToBack) return [];
        if (po is null) {
            return order.Status == SalesOrderStatus.Draft
                ? []
                : [new TimelineStepDto("supply-cancelled", "Tedarik siparişi iptal edildi", "current", null, "Satır için yeniden tedarik gerekir.")];
        }

        var poLine   = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId);
        var received = poLine?.ReceivedQuantity ?? 0m;
        var own      = receipts.Where(r => r.PurchaseOrderId == po.Id).ToList();
        var noteNo   = own.Select(r => r.SupplierDeliveryNo).LastOrDefault(n => !string.IsNullOrWhiteSpace(n));
        var tracking = string.IsNullOrWhiteSpace(po.Notes) ? null : po.Notes.Trim();

        var ordered   = po.Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received or PurchaseOrderStatus.Closed;
        var arrived   = poLine is not null && received > 0m && received >= poLine.Quantity - poLine.ClosedQuantity;
        var inTransit = received > 0m || (ordered && tracking is not null);
        var delivered = line.Quantity > 0m && line.ShippedQuantity >= line.Quantity;

        var states  = new[] { ordered, inTransit, arrived, delivered };
        var current = Array.IndexOf(states, false);
        string State(int i) => states[i] ? "done" : i == current ? "current" : "pending";

        var arrivedNote = received > 0m ? $"{received:0.######}/{poLine?.Quantity:0.######}" + (noteNo is null ? string.Empty : $" - İrsaliye {noteNo}") : null;
        return [
            new TimelineStepDto("ordered", "Tedarikçiye sipariş verildi", State(0), po.Created, po.Number),
            new TimelineStepDto("in-transit", "Kargoda", State(1), null, noteNo ?? tracking),
            new TimelineStepDto("arrived", "Geldi", State(2), null, arrivedNote),
            new TimelineStepDto("delivered", "Müşteriye teslim edildi", State(3), null, null)
        ];
    }
}
