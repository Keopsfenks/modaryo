using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.CurrentAccounts;
using ERP.Application.Inventory.Documents;
using ERP.Application.Invoicing;
using ERP.Application.Pricing.Quotes;
using ERP.Domain.Catalog;
using ERP.Domain.Invoicing;
using ERP.Domain.Pricing;
using ERP.Domain.Purchasing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Orders;

public sealed record PoDraft(Guid SupplierId, IReadOnlyList<SalesOrderLine> Lines);

/// <summary>
///     Siparişe göre tedarik: onayda, tedarikçi başına bir taslak satın alma siparişi üretir (satır SalesOrderLineId ile bağlanır).
///     PoBuilder kullanılmaz: o, alış fiyatı yetkisi ister; burada fiyat listeden çözülür, bulunamazsa 0 yazılır.
///     Plan (doğrulama) ile oluşturma ayrıdır; böylece onay, ilk yazımdan önce tüm kontrolleri bitirebilir.
/// </summary>
public sealed class BackToBackPlanner(
    IApplicationDbContext db,
    IPriceQuoteService    quotes,
    TimeProvider          clock
) {
    public async Task<IReadOnlyList<PoDraft>> PlanAsync(SalesOrder order, IReadOnlyList<SalesOrderLine> lines, IReadOnlyDictionary<Guid, Product> products, CancellationToken ct) {
        var bySupplier = new Dictionary<Guid, List<SalesOrderLine>>();
        foreach (var line in lines.OrderBy(l => l.Sort)) {
            var product  = products[line.ProductId];
            var supplier = line.SupplierId ?? product.Suppliers.FirstOrDefault(s => s.IsDefault)?.CurrentAccountId;
            if (supplier is null)
                throw SalesErrors.Bad("supplier_required", $"{line.Sort}. satır: siparişe göre tedarik için tedarikçi seçilmeli ya da kalemin varsayılan tedarikçisi olmalıdır.");

            if (!bySupplier.TryGetValue(supplier.Value, out var list)) bySupplier[supplier.Value] = list = [];
            list.Add(line);
        }

        var ids       = bySupplier.Keys.ToList();
        var suppliers = await db.CurrentAccounts.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        foreach (var id in ids) {
            var s = suppliers.GetValueOrDefault(id) ?? throw new NotFoundException(id.ToString(), "CurrentAccount");
            if (!s.IsActive) throw SalesErrors.Conflict("current_account_inactive", "Tedarikçi cari pasif; tedarik siparişi verilemez.");
            if (!s.IsSupplier) throw SalesErrors.Bad("cari_not_supplier", "Siparişe göre tedarik için cari tedarikçi olmalıdır.");
        }

        return bySupplier.Select(p => new PoDraft(p.Key, p.Value)).ToList();
    }

    /// <summary>Taslak siparişleri yazar ve satış satırlarını (PurchaseOrderId, PurchaseOrderLineId) bağlar. Açık işlem içinde çağrılır.</summary>
    public async Task CreateAsync(SalesOrder order, IReadOnlyList<PoDraft> drafts, CancellationToken ct) {
        var today = CurrentAccountClock.Today(clock);
        foreach (var draft in drafts) {
            var po = new PurchaseOrder {
                Status = PurchaseOrderStatus.Draft, SupplierId = draft.SupplierId, BranchId = order.BranchId, Date = today, Currency = "TRY", Rate = 1m,
                Notes = null
            };
            var calcLines = new List<CalcLine>();
            var poLines   = new List<PurchaseOrderLine>();
            var sort      = 1;
            foreach (var l in draft.Lines) {
                var price = await QuoteAsync(draft.SupplierId, l, today, ct);
                poLines.Add(new PurchaseOrderLine {
                    PurchaseOrderId = po.Id, Sort = sort++, ProductId = l.ProductId, VariantId = l.VariantId, Description = l.Description, Unit = l.Unit, Factor = l.Factor,
                    Quantity = l.Quantity, Width = l.Width, Length = l.Length, UnitPrice = price, VatRate = l.VatRate, SalesOrderLineId = l.Id
                });
                calcLines.Add(new CalcLine(l.Quantity, price, false, null, null, l.VatRate, null, null, null, false));
            }

            var calc = InvoiceCalculator.Calculate(calcLines, null, null);
            for (var i = 0; i < poLines.Count; i++) {
                poLines[i].Net   = calc.Lines[i].Net;
                poLines[i].Vat   = calc.Lines[i].Vat;
                poLines[i].Total = calc.Lines[i].Total;
                po.Lines.Add(poLines[i]);
            }

            po.Gross    = calc.Gross;
            po.Discount = calc.Discount;
            po.Net      = calc.Net;
            po.Vat      = calc.Vat;
            po.Total    = calc.Payable;
            po.Number   = await DocumentNumbers.NextAsync(db, "SA", today, ct);
            db.PurchaseOrders.Add(po);

            for (var i = 0; i < draft.Lines.Count; i++) {
                draft.Lines[i].PurchaseOrderId     = po.Id;
                draft.Lines[i].PurchaseOrderLineId = poLines[i].Id;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<decimal> QuoteAsync(Guid supplierId, SalesOrderLine l, DateOnly date, CancellationToken ct) {
        var quote = await quotes.QuoteAsync(new QuoteRequest(PriceListKind.Purchase, supplierId, l.ProductId, l.VariantId, l.Unit, l.Quantity, date), ct);
        if (quote is null || !string.Equals(quote.Currency, "TRY", StringComparison.OrdinalIgnoreCase)) return 0m;
        return quote.VatIncluded ? Math.Round(quote.UnitPrice / (1m + l.VatRate / 100m), 6, MidpointRounding.AwayFromZero) : quote.UnitPrice;
    }
}
