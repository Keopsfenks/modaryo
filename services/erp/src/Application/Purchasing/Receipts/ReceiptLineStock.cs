using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Inventory.Tracking;
using ERP.Domain.Catalog;
using ERP.Domain.Inventory;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Receipts;

/// <summary>
///     Mal kabul satırının stok durumu, defter hareketlerinden okunur (satır başına tek karantina durumu olduğundan kısmi işlemler
///     hareketlerle izlenir): karantina deposundan çıkan miktar, iade edilen miktar ve takipli kalemlerde kalan lot / seriler.
///     Bu okumalar stok kilitleri alındıktan SONRA yapılmalıdır.
/// </summary>
public sealed class ReceiptLineStock(IApplicationDbContext db) {
    public const string ReleaseSource = "purchase-quarantine-release";
    public const string RejectSource  = "purchase-quarantine-reject";
    public const string ReturnSource  = "purchase-return";

    /// <summary>Satırın temel birim / satır birimi oranı.</summary>
    public static decimal Factor(GoodsReceiptLine line) {
        return line.Quantity == 0m ? 1m : line.BaseQuantity / line.Quantity;
    }

    /// <summary>Karantina deposundan bu satır için çıkan temel birim miktarı (transfer, red ve iade).</summary>
    public async Task<decimal> QuarantineOutflowAsync(GoodsReceiptLine line, CancellationToken ct) {
        var sum = await db.StockMovements.AsNoTracking()
                          .Where(m => m.SourceLineId == line.Id && m.WarehouseId == line.WarehouseId && m.SourceType != ReceiptRules.StockSource && m.Quantity < 0m)
                          .SumAsync(m => (decimal?)m.Quantity, ct);
        return -(sum ?? 0m);
    }

    /// <summary>Karantinadaki açık temel birim miktarı (yalnızca Held satırlarda anlamlıdır).</summary>
    public async Task<decimal> OpenAsync(GoodsReceiptLine line, CancellationToken ct) {
        return line.Quarantine == QuarantineState.Held ? line.BaseQuantity - await QuarantineOutflowAsync(line, ct) : 0m;
    }

    /// <summary>İade edilen temel birim miktarı (karantinadan ve depodan).</summary>
    public async Task<decimal> ReturnedAsync(Guid lineId, CancellationToken ct) {
        var sum = await db.StockMovements.AsNoTracking().Where(m => m.SourceLineId == lineId && m.Type == MovementType.PurchaseReturn).SumAsync(m => (decimal?)m.Quantity, ct);
        return -(sum ?? 0m);
    }

    /// <summary>Karantinadan hasarlı depoya reddedilen temel birim miktarı (bu mal iade edilemez; hasarlı depoda kalır).</summary>
    public async Task<decimal> RejectedToDamagedAsync(Guid lineId, CancellationToken ct) {
        var sum = await db.StockMovements.AsNoTracking().Where(m => m.SourceLineId == lineId && m.SourceType == RejectSource && m.Quantity > 0m).SumAsync(m => (decimal?)m.Quantity, ct);
        return sum ?? 0m;
    }

    /// <summary>İade edilebilir temel birim miktarı = gelen - iade edilen - hasarlıya reddedilen.</summary>
    public async Task<decimal> ReturnableAsync(GoodsReceiptLine line, CancellationToken ct) {
        return line.BaseQuantity - await ReturnedAsync(line.Id, ct) - await RejectedToDamagedAsync(line.Id, ct);
    }

    /// <summary>
    ///     Serbest bırakılan miktarın şu an bulunduğu depolar: her serbest bırakmanın hedef deposuna giren miktar eksi o depodan bu satır için
    ///     yapılan iadeler; ilk serbest bırakılan depo önce. Hedef depo her bırakmada ayrı izlenir.
    /// </summary>
    public async Task<List<(Guid WarehouseId, decimal Quantity)>> ReleasedHeldAsync(Guid lineId, CancellationToken ct) {
        var moves = await db.StockMovements.AsNoTracking()
                            .Where(m => m.SourceLineId == lineId && ((m.SourceType == ReleaseSource && m.Quantity > 0m) || (m.SourceType == ReturnSource && m.Quantity < 0m)))
                            .Select(m => new { m.WarehouseId, m.Quantity, m.Sequence, m.SourceType }).ToListAsync(ct);
        return moves.GroupBy(m => m.WarehouseId)
                    .Select(g => (WarehouseId: g.Key, Quantity: g.Sum(m => m.Quantity), First: g.Where(m => m.SourceType == ReleaseSource).Select(m => (long?)m.Sequence).Min() ?? long.MaxValue))
                    .Where(x => x.Quantity > 0m).OrderBy(x => x.First).Select(x => (x.WarehouseId, x.Quantity)).ToList();
    }

    /// <summary>Satırların serbest bırakıldığı depoların (depo, kalem) anahtarları; iade kilitlerine eklenir.</summary>
    public async Task<List<(Guid, Guid, Guid?)>> ReleaseKeysAsync(IReadOnlyCollection<GoodsReceiptLine> lines, CancellationToken ct) {
        var ids   = lines.Select(l => l.Id).ToList();
        var found = await db.StockMovements.AsNoTracking().Where(m => m.SourceLineId != null && ids.Contains(m.SourceLineId.Value) && m.SourceType == ReleaseSource && m.Quantity > 0m)
                            .Select(m => new { m.WarehouseId, m.ProductId, m.VariantId }).Distinct().ToListAsync(ct);
        return found.Select(f => (f.WarehouseId, f.ProductId, f.VariantId)).ToList();
    }

    public Task<bool> AnyReleasedAsync(Guid lineId, CancellationToken ct) {
        return db.StockMovements.AnyAsync(m => m.SourceLineId == lineId && m.SourceType == ReleaseSource, ct);
    }

    /// <summary>Karantina açık miktarı bittiyse satırın durumunu kapatır (herhangi bir serbest bırakma varsa Released, yoksa Rejected).</summary>
    public async Task CloseIfResolvedAsync(GoodsReceiptLine line, CancellationToken ct) {
        if (line.Quarantine != QuarantineState.Held) return;
        if (await OpenAsync(line, ct) > 0m) return;
        line.Quarantine = await AnyReleasedAsync(line.Id, ct) ? QuarantineState.Released : QuarantineState.Rejected;
    }

    /// <summary>
    ///     Takipli kalemde karantinadaki lot ya da seriler arasından quantity (temel birim) kadarını seçer. Lot: kalan miktarı olan
    ///     lotlar sırayla; seri: hâlâ karantina deposunda olan seriler.
    /// </summary>
    public async Task<(IReadOnlyList<LotInput>? Lots, IReadOnlyList<string>? Serials)> DeriveAsync(GoodsReceiptLine line, Product product, decimal quantity,
                                                                                                    CancellationToken ct) {
        if (!TrackingValidator.IsTracked(product)) return (null, null);

        var lines = await db.StockMovements.AsNoTracking().Where(m => m.SourceLineId == line.Id && m.WarehouseId == line.WarehouseId)
                            .SelectMany(m => m.LotLines).ToListAsync(ct);
        if (TrackingValidator.ModeOf(product) == TrackingMode.Lot) {
            var left = quantity;
            var lots = new List<LotInput>();
            foreach (var group in lines.Where(l => l.LotId is not null).GroupBy(l => l.LotId!.Value)) {
                var remaining = group.Sum(l => l.Quantity);
                if (remaining <= 0m || left <= 0m) continue;
                var take = Math.Min(remaining, left);
                lots.Add(new LotInput(string.Empty, null, take, group.Key));
                left -= take;
            }

            if (left > 0m) throw PurchasingErrors.Conflict("lot_insufficient", "Karantinada yeterli lot miktarı yok.");
            return (lots, null);
        }

        var serialIds = lines.Where(l => l.SerialId is not null && l.Quantity > 0m).Select(l => l.SerialId!.Value).Distinct().ToList();
        var serials = await db.SerialNumbers.AsNoTracking().Where(s => serialIds.Contains(s.Id) && s.WarehouseId == line.WarehouseId).OrderBy(s => s.Serial)
                              .Select(s => s.Serial).ToListAsync(ct);
        if (serials.Count < quantity) throw PurchasingErrors.Conflict("serial_insufficient", "Karantinada yeterli seri numarası yok.");
        return (null, serials.Take((int)quantity).ToList());
    }
}
