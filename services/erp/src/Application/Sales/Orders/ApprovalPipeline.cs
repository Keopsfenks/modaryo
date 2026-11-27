using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Inventory.Warehouses;
using ERP.Application.Inventory;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Orders;

public enum ApprovalEntry { Approve, LimitOverride, Watcher }

public sealed record ApprovalResult(SalesOrder Order, IReadOnlyList<string> Warnings, IReadOnlyList<ShortageDto> Shortages);

/// <summary>
///     Onay kuralları sırayla: modül, kredi limiti, ön ödeme, stok. Çağıranın açık işleminde çalışır (işlem açmaz, onaylamaz).
///     Kilit sırası: cari satırı, sipariş satırı, sonra TÜM stok anahtarları tek çağrıda; bakiye okumaları kilitlerden sonradır.
///     İlk yazımdan önce tüm kontroller biter; yetersiz stokta hiçbir rezervasyon ya da satın alma siparişi yazılmaz.
/// </summary>
public sealed class ApprovalPipeline(
    IApplicationDbContext   db,
    ICurrentTenant          tenant,
    ITenantSettings         settings,
    IStockService           stock,
    BalanceReader           balances,
    DefaultWarehouseService defaults,
    BackToBackPlanner       planner,
    OrderMapper             mapper,
    SalesCurrency           currency
) {
    public async Task<ApprovalResult> RunAsync(Guid orderId, ApprovalEntry entry, CancellationToken ct) {
        var head = await db.SalesOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => new { o.CurrentAccountId, o.BranchId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(orderId.ToString(), "SalesOrder");
        BranchGuard.Ensure(tenant, head.BranchId);

        if (!await db.LockCurrentAccountAsync(head.CurrentAccountId, ct)) throw new NotFoundException(head.CurrentAccountId.ToString(), "CurrentAccount");
        if (!await db.LockSalesOrderAsync(orderId, ct)) throw new NotFoundException(orderId.ToString(), "SalesOrder");
        var order = await OrderRules.LoadAsync(db, tenant, orderId, true, ct);

        var runLimit = true;
        switch (order.Status) {
            case SalesOrderStatus.Draft:
                if (entry == ApprovalEntry.LimitOverride) throw SalesErrors.Conflict("limit_approval_not_required", "Sipariş limit onayı beklemiyor.");
                if (entry == ApprovalEntry.Watcher) return Unchanged(order);
                break;
            case SalesOrderStatus.AwaitingLimitApproval:
                if (entry == ApprovalEntry.Watcher) return Unchanged(order);
                runLimit = entry != ApprovalEntry.LimitOverride;
                break;
            case SalesOrderStatus.AwaitingPayment:
                if (entry == ApprovalEntry.LimitOverride) throw SalesErrors.Conflict("limit_approval_not_required", "Sipariş limit onayı beklemiyor.");
                runLimit = false;
                break;
            default:
                if (entry == ApprovalEntry.Watcher) return Unchanged(order);
                if (entry == ApprovalEntry.LimitOverride) throw SalesErrors.Conflict("limit_approval_not_required", "Sipariş limit onayı beklemiyor.");
                throw SalesErrors.OrderNotDraft();
        }

        // 0) Modül kapısı: siparişe göre tedarik satırı olan sipariş modül kapalıyken onaylanamaz.
        if (order.Lines.Any(l => l.SupplyMode == SupplyMode.BackToBack) && !tenant.HasModule(StockService.BackToBackModule)) {
            if (entry == ApprovalEntry.Watcher) return Unchanged(order);
            throw SalesErrors.ModuleDisabled();
        }

        var cari = await db.CurrentAccounts.AsNoTracking().FirstAsync(c => c.Id == order.CurrentAccountId, ct);

        // 1) Kredi limiti.
        if (runLimit && cari.CreditLimit is not null) {
            var balance = Statement.Balance(await db.LedgerEntries.AsNoTracking().Where(e => e.CurrentAccountId == cari.Id).ToListAsync(ct));
            var open    = await OpenOrdersAsync(order, cari.Currency, ct);
            var self    = await currency.ConvertAsync(order.Total, order.Currency, cari.Currency, order.Date, ct);
            if (CreditExposure.NeedsApproval(cari.CreditLimit, balance, open, self)) {
                order.Status = SalesOrderStatus.AwaitingLimitApproval;
                await db.SaveChangesAsync(ct);
                return new ApprovalResult(order, [], []);
            }
        }

        // 2) Ön ödeme.
        if (order.RequiresPrepayment) {
            var required = await currency.ConvertAsync(OrderRules.PrepaymentRequired(order), order.Currency, cari.Currency, order.Date, ct);
            var (_, paid) = await mapper.AdvancesAsync(order.Id, ct);
            if (paid < required) {
                order.Status = SalesOrderStatus.AwaitingPayment;
                await db.SaveChangesAsync(ct);
                return new ApprovalResult(order, [], []);
            }
        }

        // 3) Stok ve siparişe göre tedarik.
        return await ReserveAsync(order, ct);
    }

    private async Task<ApprovalResult> ReserveAsync(SalesOrder order, CancellationToken ct) {
        var productIds = order.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Include(p => p.Suppliers).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        bool Stockable(SalesOrderLine l) {
            var p = products[l.ProductId];
            return p.Kind != ProductKind.Service && !p.IsBundle;
        }

        var normal = order.Lines.Where(l => l.SupplyMode == SupplyMode.Stock && Stockable(l)).OrderBy(l => l.Sort).ToList();
        var b2b    = order.Lines.Where(l => l.SupplyMode == SupplyMode.BackToBack && Stockable(l)).OrderBy(l => l.Sort).ToList();

        Guid? fallback = null;
        foreach (var l in normal.Concat(b2b).Where(l => l.WarehouseId is null)) {
            fallback ??= (await defaults.EnsureAsync(order.BranchId, ct)).Id;
            l.WarehouseId = fallback;
        }

        var drafts = b2b.Count > 0 ? await planner.PlanAsync(order, b2b, products, ct) : [];

        // Tüm stok anahtarları tek çağrıda, bakiye okumalarından önce.
        await db.LockStockAsync(normal.Concat(b2b).Select(l => (l.WarehouseId!.Value, l.ProductId, l.VariantId)), ct);

        var warnings   = new List<string>();
        var shortages  = new List<ShortageDto>();
        var allowNeg   = (await settings.GetAsync<StockSettings>(ct)).AllowNegative;
        var groups     = normal.GroupBy(l => (Warehouse: l.WarehouseId!.Value, l.ProductId, l.VariantId)).ToList();
        if (groups.Count > 0) {
            var rows = await balances.ForItemsAsync(groups.Select(g => (g.Key.ProductId, g.Key.VariantId)).Distinct().ToList(), null, ct);
            foreach (var g in groups) {
                var required  = g.Sum(l => Base(l));
                var available = rows.FirstOrDefault(r => r.WarehouseId == g.Key.Warehouse && r.ProductId == g.Key.ProductId && r.VariantId == g.Key.VariantId)?.Available ?? 0m;
                if (available >= required) continue;
                var first = g.First();
                var p     = products[g.Key.ProductId];
                shortages.Add(new ShortageDto(first.Id, first.Sort, p.Id, $"{p.Code} - {p.Name}", g.Key.Warehouse, required, Math.Max(0m, available)));
            }
        }

        if (shortages.Count > 0) {
            if (!allowNeg) return new ApprovalResult(order, [], shortages);
            warnings.Add("insufficient_stock");
        }

        // Yazımlar.
        if (drafts.Count > 0) await planner.CreateAsync(order, drafts, ct);

        var reserve = normal.Select(l => new ReserveLine(l.WarehouseId!.Value, l.ProductId, l.VariantId, Base(l), l.Id, false, null))
                            .Concat(b2b.Select(l => new ReserveLine(l.WarehouseId!.Value, l.ProductId, l.VariantId, Base(l), l.Id, true, l.PurchaseOrderId))).ToList();
        if (reserve.Count > 0) await stock.ReserveAsync(SalesStock.SourceType, order.Id, reserve, ct);

        order.Status = SalesOrderStatus.Approved;
        await db.SaveChangesAsync(ct);
        return new ApprovalResult(order, warnings, []);
    }

    private static decimal Base(SalesOrderLine l) {
        return Math.Round(l.Quantity * l.Factor, 6, MidpointRounding.AwayFromZero);
    }

    private static ApprovalResult Unchanged(SalesOrder order) {
        return new ApprovalResult(order, [], []);
    }

    /// <summary>Diğer açık siparişlerin faturalanmamış tutarı (cari para biriminde).</summary>
    private async Task<decimal> OpenOrdersAsync(SalesOrder order, string cariCurrency, CancellationToken ct) {
        var statuses = OrderRules.OpenStatuses;
        var others = await db.SalesOrders.AsNoTracking().Include(o => o.Lines)
                             .Where(o => o.CurrentAccountId == order.CurrentAccountId && o.Id != order.Id && statuses.Contains(o.Status)).ToListAsync(ct);
        var sum = 0m;
        foreach (var o in others) {
            var open = o.Lines.Where(l => l.Quantity > 0m).Sum(l => l.Total * Math.Max(0m, l.Quantity - l.InvoicedQuantity) / l.Quantity);
            sum += await currency.ConvertAsync(decimal.Round(open, 2), o.Currency, cariCurrency, o.Date, ct);
        }

        return sum;
    }
}
