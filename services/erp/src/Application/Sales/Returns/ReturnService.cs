using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory;
using ERP.Application.Invoicing;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Returns;

/// <summary>
///     Satış iadesi belgesinin stok ve sipariş etkileri. İade belgesi oluşturulurken stok girişi (SalesReturn, asıl çıkış maliyetiyle) yazılır ve
///     P4 SalesReturn taslak faturası üretilir (taslak stok etkilemez: stok belgede girmiştir). Fatura kaynağı "sales-return", kaynak kimliği iade belgesidir.
///     İade iptalinde (belgeden ya da kesilmiş iade faturasının iptalinden) stok geri alınır ve sipariş satırının iade miktarı düşülür.
/// </summary>
public sealed class ReturnService(
    IApplicationDbContext db,
    IStockService         stock,
    IOutbox               outbox,
    TimeProvider          clock
) {
    public const string Source      = "sales-return";
    public const string StockSource = "sales-return";

    public async Task<Guid?> OrderIdAsync(Guid? returnId, CancellationToken ct) {
        if (returnId is not { } id) return null;
        return await db.SalesReturns.AsNoTracking().Where(r => r.Id == id).Select(r => r.SalesOrderId).FirstOrDefaultAsync(ct);
    }

    /// <summary>İade belgesinin stok girişlerinin kilit anahtarları (iptalde stok kilitleri önceden tek çağrıda alınır).</summary>
    public async Task<List<(Guid, Guid, Guid?)>> StockKeysAsync(Guid returnId, CancellationToken ct) {
        var keys = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == StockSource && m.SourceId == returnId && m.Quantity != 0m)
                           .Select(m => new { m.WarehouseId, m.ProductId, m.VariantId, Tracked = m.LotLines.Any() }).Distinct().ToListAsync(ct);
        return keys.Select(k => (k.WarehouseId, k.ProductId, k.VariantId))
                   .Concat(keys.Where(k => k.Tracked).Select(k => Inventory.Tracking.TrackingValidator.ItemKey(k.ProductId))).ToList();
    }

    /// <summary>Kesilmiş iade faturası iptal edildiğinde iade belgesini de geri alır (stok kilitleri poster tarafından alınmıştır).</summary>
    public async Task OnInvoiceCancelledAsync(Invoice invoice, CancellationToken ct) {
        if (invoice.SourceId is not { } id) return;
        var ret = await db.SalesReturns.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ret is null || ret.Status != SalesReturnStatus.Posted) return;
        await RevertAsync(ret, ct);
    }

    /// <summary>Stok girişini geri alır, sipariş satırlarının iade miktarını düşer ve belgeyi iptal eder. Çağıran kilitleri önceden almıştır.</summary>
    public async Task RevertAsync(SalesReturn ret, CancellationToken ct) {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
        if (await db.StockMovements.AsNoTracking().AnyAsync(m => m.SourceType == StockSource && m.SourceId == ret.Id, ct))
            await stock.ReverseAsync(StockSource, ret.Id, today, ct);

        if (ret.SalesOrderId is { } orderId) {
            var orderLines = await db.SalesOrderLines.Where(l => l.SalesOrderId == orderId).ToListAsync(ct);
            foreach (var line in ret.Lines.Where(l => l.SalesOrderLineId != null)) {
                var target = orderLines.FirstOrDefault(l => l.Id == line.SalesOrderLineId);
                if (target is null) continue;
                target.ReturnedQuantity = Math.Max(0m, target.ReturnedQuantity - Math.Round(line.Quantity * line.Factor / target.Factor, 6, MidpointRounding.AwayFromZero));
            }
        }

        ret.Status = SalesReturnStatus.Cancelled;
        outbox.Enqueue("sales.return-cancelled", new { returnId = ret.Id, number = ret.Number });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>İade faturası taslağında satır miktarı, iade belgesinde kayıtlı miktarı aşamaz.</summary>
    public async Task EnsureDraftQuantitiesAsync(Invoice invoice, IEnumerable<InvoiceLine> lines, CancellationToken ct) {
        var returnId = invoice.SourceId;
        var allowed = await db.SalesReturnLines.AsNoTracking().Where(l => l.SalesReturnId == returnId).ToListAsync(ct);
        foreach (var group in lines.Where(l => l.SourceLineId != null).GroupBy(l => l.SourceLineId!.Value)) {
            var limit = allowed.Where(a => a.InvoiceLineId == group.Key).Sum(a => a.Quantity * a.Factor);
            if (group.Sum(l => l.Quantity * l.Factor) > limit + 0.001m)
                throw SalesErrors.Bad("return_exceeds_invoiced", "İade faturası satırı, iade belgesinde kayıtlı miktarı aşamaz.");
        }
    }

    public static void EnsureDraftFieldsLocked(Invoice invoice, InvoiceInput input) {
        if (input.Kind != invoice.Kind || input.ReturnOfInvoiceId != invoice.ReturnOfInvoiceId)
            throw InvoiceErrors.Bad("sales_invoice_locked", "İade belgesinden gelen faturanın türü ve asıl faturası değiştirilemez.");
        if (input.AffectsStock)
            throw InvoiceErrors.Bad("sales_invoice_affects_stock", "İade belgesinden gelen faturada \"Stoğa işle\" açılamaz; stok iade belgesinde girmiştir.");
        if (input.CurrentAccountId != invoice.CurrentAccountId || input.BranchId != invoice.BranchId)
            throw InvoiceErrors.Bad("sales_invoice_locked", "İade belgesinden gelen faturanın müşterisi ve şubesi değiştirilemez.");
        if (!string.Equals(input.Currency?.Trim(), invoice.Currency, StringComparison.OrdinalIgnoreCase))
            throw InvoiceErrors.Bad("sales_invoice_locked", "İade belgesinden gelen faturanın para birimi değiştirilemez.");
        if (input.Lines.Any(l => l.SourceLineId is null))
            throw InvoiceErrors.Bad("return_line_required", "İade satırı asıl faturanın bir satırına bağlanmalıdır.");
    }
}
