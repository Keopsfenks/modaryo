using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.CurrentAccounts;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.Treasury.Allocations;

public sealed record AllocationItem(Guid DueLineId, decimal Amount);

/// <summary>
///     Elle eşleştirmenin tek yazıcısı. Çağıran açık bir işlem içinde ve carinin kilidi alınmış olarak çağırır; doğrulama kilit altında
///     okunan saklı eşleştirmelerle yapıldığından aynı kalemi hedefleyen paralel istekler birbirini görür (fazla eşleştirme olmaz).
///     Kapasite = satır tutarı - satırın ELLE eşleştirmeleri: otomatik eşleştirmeler zaten yeniden kurulur ve elle olanın önüne geçemez.
/// </summary>
public sealed class ManualAllocator(
    IApplicationDbContext db,
    AutoAllocator         auto,
    TimeProvider          time
) {
    private sealed record LineInfo(LedgerDueLine Line, bool Cancelled);

    /// <summary>Kaynak satır (tahsilat/ödeme satırı) ile karşı yöndeki vade satırlarını eşleştirir; sonra otomatik eşleştirmeleri yeniden kurar.</summary>
    public async Task AllocateAsync(Guid currentAccountId, Guid sourceLineId, IReadOnlyList<AllocationItem> items, CancellationToken ct) {
        if (items.Count == 0) throw TreasuryErrors.AllocationInvalid("En az bir eşleştirme satırı gerekir.");

        foreach (var item in items) {
            if (item.Amount <= 0m || !CardRules.HasValidScale(item.Amount))
                throw TreasuryErrors.AllocationInvalid("Eşleştirme tutarı sıfırdan büyük olmalı ve en fazla 2 ondalık basamak içermelidir.");
        }

        if (items.Select(i => i.DueLineId).Distinct().Count() != items.Count)
            throw TreasuryErrors.AllocationInvalid("Aynı vade satırı birden çok kez verilemez.");

        if (items.Any(i => i.DueLineId == sourceLineId)) throw TreasuryErrors.AllocationDirection();

        var ids = items.Select(i => i.DueLineId).Append(sourceLineId).ToList();
        var rows = await (from l in db.LedgerDueLines.AsNoTracking()
                          join e in db.LedgerEntries on l.EntryId equals e.Id
                          where ids.Contains(l.Id)
                          select new { Line = l, Cancelled = e.CancelledAt != null }).ToListAsync(ct);
        var lines = rows.ToDictionary(r => r.Line.Id, r => new LineInfo(r.Line, r.Cancelled));
        foreach (var id in ids)
            if (!lines.ContainsKey(id)) throw new NotFoundException(id.ToString(), "LedgerDueLine");

        if (lines.Values.Any(l => l.Line.CurrentAccountId != currentAccountId)) throw TreasuryErrors.AllocationOtherAccount();

        if (lines.Values.Any(l => l.Cancelled)) throw TreasuryErrors.AllocationLineCancelled();

        var source = lines[sourceLineId].Line;
        if (items.Any(i => lines[i.DueLineId].Line.Direction == source.Direction)) throw TreasuryErrors.AllocationDirection();

        // Elle eşleştirilmiş tutarlar (kilit altında okunur).
        var manual = await db.LedgerAllocations.AsNoTracking()
                         .Where(a => a.CurrentAccountId == currentAccountId && a.Kind == AllocationKind.Manual &&
                                     (ids.Contains(a.DebitLineId) || ids.Contains(a.CreditLineId)))
                         .Select(a => new { a.DebitLineId, a.CreditLineId, a.Amount }).ToListAsync(ct);

        decimal Taken(Guid lineId) {
            return manual.Where(a => a.DebitLineId == lineId || a.CreditLineId == lineId).Sum(a => a.Amount);
        }

        var total     = items.Sum(i => i.Amount);
        var available = source.Amount - Taken(sourceLineId);
        if (total > available)
            throw TreasuryErrors.AllocationExceedsOpen($"Eşleştirilen toplam ({total:N2}), kaynak satırın eşleştirilmemiş tutarını ({Math.Max(0m, available):N2}) aşıyor.");

        foreach (var item in items) {
            var line = lines[item.DueLineId].Line;
            var open = line.Amount - Taken(line.Id);
            if (item.Amount > open)
                throw TreasuryErrors.AllocationExceedsOpen($"Eşleştirme tutarı ({item.Amount:N2}), satırın açık tutarını ({Math.Max(0m, open):N2}) aşıyor.");
        }

        var now = time.GetUtcNow();
        foreach (var item in items) {
            var target = lines[item.DueLineId].Line;
            var debit  = source.Direction == DueDirection.Receivable ? source : target;
            var credit = source.Direction == DueDirection.Receivable ? target : source;
            db.LedgerAllocations.Add(new LedgerAllocation {
                                                              TenantId         = source.TenantId,
                                                              CurrentAccountId = currentAccountId,
                                                              DebitLineId      = debit.Id,
                                                              CreditLineId     = credit.Id,
                                                              Amount           = item.Amount,
                                                              Kind             = AllocationKind.Manual,
                                                              CreatedAt        = now
                                                          });
        }

        await db.SaveChangesAsync(ct);
        await auto.RebuildAsync(currentAccountId, ct);
        await db.SaveChangesAsync(ct);
    }
}
