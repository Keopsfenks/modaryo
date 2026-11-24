using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Inventory;
using Microsoft.Extensions.Logging;

namespace ERP.Application.Inventory.Costing;

/// <summary>Arka plan işi için firma bağlamı: ICurrentTenant bu değeri taşıyorsa HTTP oturumu yerine kullanılır.</summary>
public sealed class TenantScope {
    public ICurrentTenant? Override { get; set; }
}

public sealed class BackgroundTenant(
    Guid tenantId
) : ICurrentTenant {
    public Guid? TenantId => tenantId;

    public Guid? UserId => null;

    public string? UserFullName => "sistem:stok";

    public bool HasModule(string key) {
        return false;
    }

    public IReadOnlyList<Guid>? BranchIds => null;

    public bool CanAccessBranch(Guid branchId) {
        return true;
    }

    public decimal MaxDiscountPercent => 0m;
}

public interface IRevaluationRunner {
    /// <summary>Aktif firmanın bekleyen tüm işlerini işler (transfer yayılımıyla doğan yeni işler dahil). İşlenen iş sayısını döndürür.</summary>
    Task<int> RunPendingAsync(CancellationToken ct);
}

/// <summary>Geriye dönük yazılan hareketlerin işlerini işler: depo x kalemi FromDate'ten itibaren yeniden maliyetler.</summary>
internal sealed class RevaluationRunner : IRevaluationRunner {
    internal const string CostRevalued = "stock.cost-revalued";

    private const int MaxRounds = 20;

    private readonly IApplicationDbContext      db;
    private readonly ITenantSettings            settings;
    private readonly IOutbox                    outbox;
    private readonly StockLedger                ledger;
    private readonly ILogger<RevaluationRunner> logger;

    public RevaluationRunner(IApplicationDbContext db, ITenantSettings settings, IOutbox outbox, StockLedger ledger,
                               ILogger<RevaluationRunner> logger) {
        this.db       = db;
        this.settings = settings;
        this.outbox   = outbox;
        this.ledger   = ledger;
        this.logger   = logger;
    }

    public async Task<int> RunPendingAsync(CancellationToken ct) {
        var processed = 0;
        var failed    = new HashSet<Guid>();
        for (var round = 0; round < MaxRounds; round++) {
            db.ClearTracking();
            var ids = await db.RevaluationJobs.AsNoTracking().OrderBy(j => j.Id).Select(j => j.Id).ToListAsync(ct);
            ids = ids.Where(id => !failed.Contains(id)).ToList();
            if (ids.Count == 0) break;

            foreach (var id in ids) {
                db.ClearTracking();
                try {
                    if (await RunJobAsync(id, ct)) processed++;
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    failed.Add(id);
                    logger.LogError(e, "Yeniden değerleme işi {JobId} çalıştırılamadı.", id);
                }
            }
        }

        db.ClearTracking();
        return processed;
    }

    private async Task<bool> RunJobAsync(Guid jobId, CancellationToken ct) {
        var seed = await db.RevaluationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (seed is null) return false;

        var source = (seed.WarehouseId, seed.ProductId, seed.VariantId);

        // Kilit kümesi = kaynak anahtar + bu anahtardaki transfer çıkışlarının karşı depo anahtarları. Karşı depodaki giriş ve
        // iş kaydı da değiştirileceğinden hepsi tek sıralı çağrıda baştan kilitlenir. Kilit beklerken yeni transfer eklenmişse
        // (küme büyüdü) işlem geri alınır ve büyüyen kümeyle yeniden denenir.
        var wanted = new HashSet<(Guid, Guid, Guid?)> { source };
        for (var attempt = 0; ; attempt++) {
            foreach (var key in await DestinationKeysAsync(source, ct)) wanted.Add(key);

            var transaction = await db.BeginTransactionAsync(ct);
            await using (transaction) {
                await db.LockStockAsync(wanted, ct);

                var current = await DestinationKeysAsync(source, ct);
                if (current.All(wanted.Contains)) return await RunLockedAsync(jobId, transaction, ct);

                await transaction.RollbackAsync(ct);
            }

            db.ClearTracking();
            if (attempt >= 5) throw new InvalidOperationException("Yeniden değerleme kilit kümesi sabitlenemedi.");
        }
    }

    /// <summary>Bu depo x kalemdeki transfer çıkışlarının karşı girişlerinin (depo, ürün, varyant) anahtarları.</summary>
    private async Task<List<(Guid, Guid, Guid?)>> DestinationKeysAsync((Guid WarehouseId, Guid ProductId, Guid? VariantId) source,
                                                                         CancellationToken ct) {
        var outs = await db.StockMovements.AsNoTracking()
                           .Where(m => m.WarehouseId == source.WarehouseId && m.ProductId == source.ProductId && m.VariantId == source.VariantId
                                       && m.Type == MovementType.TransferOut && m.Quantity < 0m)
                           .Select(m => m.Id).ToListAsync(ct);
        var keys = new HashSet<(Guid, Guid, Guid?)>();
        foreach (var chunk in outs.Chunk(500)) {
            var notes = chunk.Select(id => CostReplay.TransferPrefix + id).ToList();
            var found = await db.StockMovements.AsNoTracking().Where(m => m.Quantity > 0m && m.Note != null && notes.Contains(m.Note))
                                .Select(m => new { m.WarehouseId, m.ProductId, m.VariantId }).Distinct().ToListAsync(ct);
            foreach (var f in found) keys.Add((f.WarehouseId, f.ProductId, f.VariantId));
        }

        keys.Remove((source.WarehouseId, source.ProductId, source.VariantId));
        return keys.ToList();
    }

    private async Task<bool> RunLockedAsync(Guid jobId, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct) {
        var job = await db.RevaluationJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) {
            await transaction.CommitAsync(ct);
            return false;
        }

        var method   = (await settings.GetAsync<StockSettings>(ct)).Method;
        var lockDate = (await settings.GetAsync<FinanceSettings>(ct)).PeriodLockDate;
        var from     = job.FromDate;

        var movements = await db.StockMovements
                                .Where(m => m.WarehouseId == job.WarehouseId && m.ProductId == job.ProductId && m.VariantId == job.VariantId)
                                .OrderBy(m => m.Date).ThenBy(m => m.Sequence).ToListAsync(ct);

        bool Frozen(StockMovement m) {
            return m.Date < from || (lockDate is { } locked && m.Date <= locked);
        }

        var result  = CostReplay.Run(movements, method, Frozen);
        var changed = new List<StockMovement>();
        foreach (var m in movements) {
            if (Frozen(m) || m.Quantity == 0m || !result.Costs.TryGetValue(m.Id, out var cost)) continue;

            var sign     = m.Quantity < 0m ? -1m : 1m;
            var newTotal = sign * cost.TotalCost;
            if (m.UnitCost == cost.UnitCost && m.TotalCost == newTotal) continue;

            Publish(m, cost.UnitCost, newTotal);
            m.UnitCost  = cost.UnitCost;
            m.TotalCost = newTotal;
            changed.Add(m);
        }

        await ApplyStateAsync(job, method, result, ct);

        // Yeniden maliyetlenen transfer çıkışı, karşı depodaki girişi de değiştirir; o depo x kalem için iş açılır.
        foreach (var outbound in changed.Where(m => m.Type == MovementType.TransferOut && m.Quantity < 0m))
            await PropagateTransferAsync(outbound, ct);

        db.RevaluationJobs.Remove(job);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private void Publish(StockMovement m, decimal newUnit, decimal newTotal) {
        outbox.Enqueue(CostRevalued,
                       new CostRevaluedData(m.Id, m.WarehouseId, m.ProductId, m.VariantId, m.Date, m.Type.ToString(), m.SourceType,
                                            m.SourceId, m.UnitCost, newUnit, m.TotalCost, newTotal));
    }

    private async Task PropagateTransferAsync(StockMovement outbound, CancellationToken ct) {
        var note   = CostReplay.TransferPrefix + outbound.Id;
        var inbound = await db.StockMovements.FirstOrDefaultAsync(m => m.Note == note && m.Quantity > 0m, ct);
        if (inbound is null) return;

        var unit  = outbound.UnitCost;
        var total = CostReplay.Round2(inbound.Quantity * unit);
        if (inbound.UnitCost == unit && inbound.TotalCost == total) return;

        Publish(inbound, unit, total);
        inbound.UnitCost  = unit;
        inbound.TotalCost = total;
        await ledger.EnqueueRevaluationAsync(inbound.WarehouseId, inbound.ProductId, inbound.VariantId, inbound.Date, ct);
    }

    /// <summary>Anlık ortalama ve katmanlar baştan hesaplanan değerlerle yeniden yazılır.</summary>
    private async Task ApplyStateAsync(RevaluationJob job, CostingMethod method, ReplayResult result, CancellationToken ct) {
        var snapshot = await db.AverageCosts.FirstOrDefaultAsync(s => s.WarehouseId == job.WarehouseId && s.ProductId == job.ProductId
                                                                      && s.VariantId == job.VariantId, ct);
        if (snapshot is null) {
            snapshot = new AverageCostSnapshot { WarehouseId = job.WarehouseId, ProductId = job.ProductId, VariantId = job.VariantId };
            db.AverageCosts.Add(snapshot);
        }

        snapshot.Quantity    = result.Quantity;
        snapshot.AverageCost = result.AverageCost;
        if (method != CostingMethod.Fifo) return;

        var existing = await db.CostLayers
                               .Where(l => l.WarehouseId == job.WarehouseId && l.ProductId == job.ProductId && l.VariantId == job.VariantId)
                               .ToListAsync(ct);
        foreach (var replayed in result.Layers) {
            var layer = existing.FirstOrDefault(l => l.InboundMovementId == replayed.MovementId);
            if (layer is null) {
                db.CostLayers.Add(new CostLayer {
                    WarehouseId = job.WarehouseId, ProductId = job.ProductId, VariantId = job.VariantId,
                    InboundMovementId = replayed.MovementId, Date = replayed.Date, Sequence = replayed.Sequence,
                    OriginalQty = replayed.Original, RemainingQty = replayed.Remaining, UnitCost = replayed.UnitCost
                });
                continue;
            }

            layer.Date         = replayed.Date;
            layer.Sequence     = replayed.Sequence;
            layer.OriginalQty  = replayed.Original;
            layer.RemainingQty = replayed.Remaining;
            layer.UnitCost     = replayed.UnitCost;
        }

        foreach (var stale in existing.Where(l => result.Layers.All(r => r.MovementId != l.InboundMovementId)))
            db.CostLayers.Remove(stale);
    }
}

internal sealed record CostRevaluedData(
    Guid     MovementId,
    Guid     WarehouseId,
    Guid     ProductId,
    Guid?    VariantId,
    DateOnly Date,
    string   MovementType,
    string   SourceType,
    Guid     SourceId,
    decimal  OldUnitCost,
    decimal  NewUnitCost,
    decimal  OldTotalCost,
    decimal  NewTotalCost
);
