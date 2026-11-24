using ERP.Application.Common.Interfaces;
using ERP.Application.Invoicing;
using ERP.Domain.Invoicing;
using Microsoft.Extensions.Logging;

namespace ERP.Application.Rates;

public sealed record RateImportResult(int Stored, int Failed);

/// <summary>TCMB kurlarını veritabanına alır: bugünün bülteni ve son 10 günün eksik iş günleri. Elle kurlara dokunmaz.</summary>
public static class RateImport {
    public const int BackfillDays = 10;

    public static async Task<RateImportResult> ImportAsync(IApplicationDbContext db, ITcmbRateSource source, DateOnly today, ILogger logger,
                                                           CancellationToken ct) {
        var stored = 0;
        var failed = 0;

        // Bugünün bülteni: today.xml içindeki tarih esas alınır (15:30 öncesi önceki günün bülteni olabilir).
        var attempts = new List<DateOnly?> { null };
        var from     = today.AddDays(-BackfillDays);
        var have = (await db.ExchangeRates.AsNoTracking().Where(r => r.Date >= from && r.Type == RateType.ForexBuying && r.Currency == "USD")
                        .Select(r => r.Date).ToListAsync(ct)).ToHashSet();
        for (var day = today.AddDays(-1); day >= from; day = day.AddDays(-1)) {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || have.Contains(day)) continue;
            attempts.Add(day);
        }

        foreach (var date in attempts) {
            try {
                var rows = await source.FetchAsync(date, ct);
                if (rows is null) continue;
                stored += await StoreAsync(db, rows, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                failed++;
                logger.LogWarning(e, "TCMB kurları alınamadı ({Date}); sonraki turda yeniden denenecek.", date?.ToString("yyyy-MM-dd") ?? "bugün");
            }
        }

        return new RateImportResult(stored, failed);
    }

    /// <summary>Var olan (para birimi, tür, gün) satırlarını günceller, olmayanları ekler.</summary>
    public static async Task<int> StoreAsync(IApplicationDbContext db, IReadOnlyList<ExchangeRate> rows, CancellationToken ct) {
        if (rows.Count == 0) return 0;
        var dates    = rows.Select(r => r.Date).Distinct().ToList();
        var existing = await db.ExchangeRates.Where(r => dates.Contains(r.Date)).ToListAsync(ct);
        var changed  = 0;
        foreach (var row in rows) {
            var current = existing.FirstOrDefault(e => e.Currency == row.Currency && e.Type == row.Type && e.Date == row.Date);
            if (current is null) {
                db.ExchangeRates.Add(new ExchangeRate { Currency = row.Currency, Date = row.Date, Type = row.Type, Rate = row.Rate, Source = row.Source });
                changed++;
            } else if (current.Rate != row.Rate) {
                current.Rate = row.Rate;
                changed++;
            }
        }

        try {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (InvoicingErrors.IsUniqueViolation(ex)) {
            // Başka bir süreç aynı bülteni az önce yazdı.
            db.ClearTracking();
            return 0;
        }

        return changed;
    }
}
