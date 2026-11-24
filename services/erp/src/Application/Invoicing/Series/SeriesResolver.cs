using ERP.Application.Common.Interfaces;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing.Series;

/// <summary>
///     Varsayılan kâğıt serisi önekleri. Önek firmada benzersizdir (numaralar firma genelinde tekil); 3 karakter [A-Z0-9].
///     Ana şube: satış "SAT", alış "ALS". Diğer şubeler: satış "S" / alış "A" + şube kimliğinden türetilen iki base-36 karakter;
///     çakışmada sıradaki aday denenir. Aynı girdi her zaman aynı öneki verir.
/// </summary>
public static class DefaultSeriesPrefixes {
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Pick(string kindGroup, Guid branchId, bool isMainBranch, IReadOnlySet<string> taken) {
        var purchase = kindGroup == "purchase";
        if (isMainBranch) {
            var fixedPrefix = purchase ? "ALS" : "SAT";
            if (!taken.Contains(fixedPrefix)) return fixedPrefix;
        }

        var lead  = purchase ? 'A' : 'S';
        var start = BitConverter.ToUInt32(branchId.ToByteArray(), 12) % 1296u;
        for (var i = 0u; i < 1296u; i++) {
            var n         = (start + i) % 1296u;
            var candidate = string.Concat(lead, Digits[(int)(n / 36)], Digits[(int)(n % 36)]);
            if (!taken.Contains(candidate)) return candidate;
        }

        throw new InvalidOperationException("Varsayılan seri için boş önek kalmadı.");
    }
}

/// <summary>
///     Kesimde numara serisini çözer: (grup, şube, mod) varsayılanı; belge yılı için aynı öneke sahip satırı bulur ya da açar
///     (yıl devri, sıra 1'den). Varsayılan seri yoksa kâğıt modunda şube için tembel olarak oluşturur
///     (DefaultWarehouseService ile aynı yaklaşım); e-belge serileri GİB'e bildirilen önek gerektirdiğinden elle tanımlanır.
///     Eşzamanlı çağrılar INSERT ... ON CONFLICT DO NOTHING ile çakışmaz; çağıran açık bir işlem içinde olmalıdır.
/// </summary>
public sealed class SeriesResolver(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IBranchDirectory      branches
) {
    public async Task<DocumentSeries> ResolveAsync(string group, Guid branchId, InvoiceMode mode, int year, CancellationToken ct) {
        var def = await FindDefaultAsync(group, branchId, mode, ct);
        if (def is null) {
            if (mode != InvoiceMode.Paper) throw InvoicingErrors.SeriesMissing();
            def = await ProvisionDefaultPaperAsync(group, branchId, year, ct);
        }

        return def.Year == year ? def : await FindOrRollOverAsync(def, year, ct);
    }

    private Task<DocumentSeries?> FindDefaultAsync(string group, Guid branchId, InvoiceMode mode, CancellationToken ct) {
        return db.DocumentSeries.AsNoTracking()
                 .FirstOrDefaultAsync(s => s.IsDefault && s.IsActive && s.KindGroup == group && s.BranchId == branchId && s.Mode == mode, ct);
    }

    private async Task<DocumentSeries> FindOrRollOverAsync(DocumentSeries def, int year, CancellationToken ct) {
        async Task<DocumentSeries?> Find() => await db.DocumentSeries.AsNoTracking()
                                                      .FirstOrDefaultAsync(s => s.KindGroup == def.KindGroup && s.BranchId == def.BranchId && s.Mode == def.Mode
                                                                                && s.Prefix == def.Prefix && s.Year == year, ct);

        var existing = await Find();
        if (existing is null) {
            await db.TryInsertSeriesAsync(new DocumentSeries {
                KindGroup = def.KindGroup, BranchId = def.BranchId, Mode = def.Mode, Prefix = def.Prefix, Year = year, MinWidth = def.MinWidth,
                IsDefault = false, IsActive = true
            }, ct);
            existing = await Find() ?? throw InvoicingErrors.PrefixTaken(def.Prefix);
        }

        return existing.IsActive
                   ? existing
                   : throw InvoiceErrors.Conflict("series_missing", $"{def.Prefix} önekinin {year} serisi pasif; etkinleştirin ya da yeni bir seri tanımlayın.");
    }

    private async Task<DocumentSeries> ProvisionDefaultPaperAsync(string group, Guid branchId, int year, CancellationToken ct) {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var branch = (await branches.ListAsync(tenantId, ct)).FirstOrDefault(b => b.Id == branchId && b.IsActive)
                     ?? throw InvoicingErrors.BranchNotFound();

        // Başka bir istek aynı anda aynı öneki aldıysa (başka şube çakışması) sıradaki adayla yeniden denenir.
        for (var attempt = 0; attempt < 8; attempt++) {
            var existing = await FindDefaultAsync(group, branchId, InvoiceMode.Paper, ct);
            if (existing is not null) return existing;

            var taken  = (await db.DocumentSeries.AsNoTracking().Select(s => s.Prefix).Distinct().ToListAsync(ct)).ToHashSet();
            var prefix = DefaultSeriesPrefixes.Pick(group, branchId, branch.IsDefault, taken);
            await db.TryInsertSeriesAsync(new DocumentSeries {
                KindGroup = group, BranchId = branchId, Mode = InvoiceMode.Paper, Prefix = prefix, Year = year, MinWidth = 9, IsDefault = true, IsActive = true
            }, ct);
        }

        throw InvoicingErrors.SeriesMissing();
    }
}
