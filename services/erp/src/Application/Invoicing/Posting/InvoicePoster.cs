using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Inventory.Tracking;
using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Posting;

/// <summary>
///     Kesilen faturanın defter, vade ve stok etkilerinin tek yazıcısı. Çağıran açık bir işlem içinde ve carinin kilidi
///     alınmış olarak çağırır, işlemi kendisi onaylar. Sıra: cari kilidi (çağıran), tüm stok kilitleri (tek seferde, sıralı),
///     defter, vade, eşleştirme, stok, outbox. İptal aynı sırayı izler ve yalnızca bu etkileri geri alır.
/// </summary>
public sealed class InvoicePoster(
    IApplicationDbContext db,
    CurrencyPosting       currency,
    AutoAllocator         allocator,
    IStockService         stock,
    ICurrentSession       session,
    TimeProvider          clock,
    IOutbox               outbox
) {
    public const string StockSource = "invoice";

    /// <summary>Satış, kur farkı ve alış iadesi cari hesapta borç; alış ve satış iadesi alacaktır.</summary>
    public static bool IsDebit(InvoiceKind kind) {
        return kind is InvoiceKind.Sales or InvoiceKind.ExchangeDifference or InvoiceKind.PurchaseReturn;
    }

    private static string Describe(Invoice invoice) {
        var title = invoice.Kind switch {
                        InvoiceKind.Sales          => "Satış faturası",
                        InvoiceKind.SalesReturn    => "Satış iade faturası",
                        InvoiceKind.Purchase       => "Alış faturası",
                        InvoiceKind.PurchaseReturn => "Alış iade faturası",
                        _                          => "Kur farkı faturası"
                    };
        return invoice.ExternalNumber is { Length: > 0 } external ? $"{title} (Tedarikçi no: {external})" : title;
    }

    public async Task PostAsync(Invoice invoice, CancellationToken ct) {
        var cari = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CurrentAccountId, ct)
                   ?? throw new NotFoundException(invoice.CurrentAccountId.ToString(), "CurrentAccount");

        // Stok kilitleri: tüm anahtarlar tek seferde, bakiye okumalarından önce.
        var plan = invoice.AffectsStock ? await PlanStockAsync(invoice, ct) : new StockPlan([], []);
        if (plan.Lines.Count > 0) await db.LockStockAsync(plan.Keys, ct);

        var posted = await currency.ConvertAsync(invoice, cari.Currency, ct);
        var dues   = BuildDueLines(invoice, posted.Amount);

        if (posted.Amount > 0m) {
            var debit = IsDebit(invoice.Kind);
            var entry = new LedgerEntry {
                                            TenantId         = invoice.TenantId,
                                            CurrentAccountId = cari.Id,
                                            Type             = debit ? LedgerEntryType.DebitNote : LedgerEntryType.CreditNote,
                                            Source           = LedgerEntrySource.Invoice,
                                            SourceId         = invoice.Id,
                                            Date             = invoice.Date,
                                            DueDate          = dues.Min(d => d.DueDate),
                                            DocumentNo       = invoice.Number,
                                            Description      = Describe(invoice),
                                            Debit            = debit ? posted.Amount : 0m,
                                            Credit           = debit ? 0m : posted.Amount,
                                            Currency         = posted.Currency,
                                            SourceCurrency   = posted.SourceCurrency,
                                            SourceAmount     = posted.SourceAmount,
                                            Rate             = posted.Rate
                                        };
            db.LedgerEntries.Add(entry);
            foreach (var due in dues)
                db.LedgerDueLines.Add(new LedgerDueLine {
                                                            TenantId         = invoice.TenantId,
                                                            EntryId          = entry.Id,
                                                            CurrentAccountId = cari.Id,
                                                            DueDate          = due.DueDate,
                                                            Amount           = due.Amount,
                                                            Direction        = debit ? DueDirection.Receivable : DueDirection.Payable,
                                                            Sequence         = due.Sequence
                                                        });
            invoice.LedgerEntryId = entry.Id;
            await db.SaveChangesAsync(ct);

            await allocator.RebuildAsync(cari.Id, ct);
            await db.SaveChangesAsync(ct);
        }

        if (plan.Lines.Count > 0) await PostStockAsync(invoice, plan, ct);

        outbox.Enqueue("invoice.issued", new { invoiceId = invoice.Id, kind = invoice.Kind.ToString(), mode = invoice.Mode.ToString(), number = invoice.Number });
    }

    public async Task ReverseAsync(Invoice invoice, string reason, CancellationToken ct) {
        // Stok kilitleri tek seferde, defter değişikliklerinden önce.
        var keys = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == StockSource && m.SourceId == invoice.Id && m.Quantity != 0m)
                           .Select(m => new { m.WarehouseId, m.ProductId, m.VariantId, Tracked = m.LotLines.Any() }).Distinct().ToListAsync(ct);
        if (keys.Count > 0)
            await db.LockStockAsync(keys.Select(k => (k.WarehouseId, k.ProductId, k.VariantId))
                                        .Concat(keys.Where(k => k.Tracked).Select(k => TrackingValidator.ItemKey(k.ProductId))), ct);

        if (invoice.LedgerEntryId is { } entryId) {
            var entry = await db.LedgerEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct) ?? throw new NotFoundException(entryId.ToString(), "LedgerEntry");
            if (!entry.IsCancelled) {
                entry.CancelledAt  = clock.GetUtcNow();
                entry.CancelledBy  = session.UserId?.ToString();
                entry.CancelReason = reason;
                await db.SaveChangesAsync(ct);
            }

            await allocator.RebuildAsync(invoice.CurrentAccountId, ct);
            await db.SaveChangesAsync(ct);
        }

        if (keys.Count > 0) await stock.ReverseAsync(StockSource, invoice.Id, invoice.Date, ct);

        outbox.Enqueue("invoice.cancelled", new { invoiceId = invoice.Id, kind = invoice.Kind.ToString(), mode = invoice.Mode.ToString(), number = invoice.Number, reason });
    }

    // ---- vade --------------------------------------------------------------------------------------------------

    private sealed record Due(int Sequence, DateOnly DueDate, decimal Amount);

    private static List<Due> BuildDueLines(Invoice invoice, decimal ledgerAmount) {
        if (invoice.DueSchedule.Count == 0) return [new Due(1, invoice.Date.AddDays(invoice.PaymentTermDays), ledgerAmount)];

        if (invoice.DueSchedule.Sum(d => d.Amount) != ledgerAmount)
            throw InvoiceErrors.Bad("due_schedule_mismatch", "Vade planı toplamı cari para birimindeki fatura tutarına eşit olmalıdır (kur değişmiş olabilir; taslağı güncelleyin).");

        return invoice.DueSchedule.OrderBy(d => d.DueDate).ThenBy(d => d.Sort).Select((d, i) => new Due(i + 1, d.DueDate, d.Amount)).ToList();
    }

    // ---- stok --------------------------------------------------------------------------------------------------

    private sealed record StockPlan(List<StockLine> Lines, List<(Guid, Guid, Guid?)> Keys);

    private async Task<StockPlan> PlanStockAsync(Invoice invoice, CancellationToken ct) {
        var plan = new StockPlan([], []);
        if (invoice.Kind == InvoiceKind.ExchangeDifference || invoice.WarehouseId is not { } warehouse) return plan;

        var stockable = invoice.Lines.Where(l => l.ProductId is not null).ToList();
        if (stockable.Count == 0) return plan;

        var ids          = stockable.Select(l => l.ProductId!.Value).Distinct().ToList();
        var products     = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var componentIds = products.Values.Where(p => p.IsBundle).SelectMany(p => p.BundleComponents.Select(c => c.ProductId)).Distinct().ToList();
        var components   = componentIds.Count == 0 ? [] : await db.Products.AsNoTracking().Where(p => componentIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var purchase = invoice.Kind == InvoiceKind.Purchase;
        foreach (var line in stockable) {
            var product = products[line.ProductId!.Value];
            if (product.Kind == ProductKind.Service) continue; // hizmet satırı stoğu etkilemez

            var baseQuantity = Math.Round(line.Quantity * line.Factor, 6, MidpointRounding.AwayFromZero);
            if (baseQuantity <= 0m) continue;

            // Alış: satır net TL maliyeti (KDV hariç, ÖTV dahil) / temel birim miktarı.
            decimal? unitCost = purchase ? Math.Round((line.NetTl + line.OtvTl) / baseQuantity, 6, MidpointRounding.AwayFromZero) : null;
            var      lots     = line.Lots?.Select(l => new LotInput(l.LotNo, l.Expiry, l.Quantity, l.LotId)).ToList();
            plan.Lines.Add(new StockLine(warehouse, product.Id, line.VariantId, baseQuantity, unitCost, lots, null, line.Id));

            if (product.IsBundle) {
                foreach (var component in product.BundleComponents) {
                    plan.Keys.Add((warehouse, component.ProductId, component.VariantId));
                    if (components.TryGetValue(component.ProductId, out var c) && TrackingValidator.IsTracked(c)) plan.Keys.Add(TrackingValidator.ItemKey(c.Id));
                }
            } else {
                plan.Keys.Add((warehouse, product.Id, line.VariantId));
                if (TrackingValidator.IsTracked(product)) plan.Keys.Add(TrackingValidator.ItemKey(product.Id));
            }
        }

        return plan;
    }

    private async Task PostStockAsync(Invoice invoice, StockPlan plan, CancellationToken ct) {
        switch (invoice.Kind) {
            case InvoiceKind.Sales:
                await stock.IssueAsync(MovementType.SalesShipment, StockSource, invoice.Id, invoice.Date, plan.Lines, ct);
                break;
            case InvoiceKind.PurchaseReturn:
                await stock.IssueAsync(MovementType.PurchaseReturn, StockSource, invoice.Id, invoice.Date, plan.Lines, ct);
                break;
            case InvoiceKind.SalesReturn:
                await stock.ReceiveAsync(MovementType.SalesReturn, StockSource, invoice.Id, invoice.Date, plan.Lines, ct);
                break;
            case InvoiceKind.Purchase:
                await stock.ReceiveAsync(MovementType.PurchaseReceipt, StockSource, invoice.Id, invoice.Date, plan.Lines, ct);
                break;
        }
    }
}
