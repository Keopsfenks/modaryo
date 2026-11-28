using System.Text.Json;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Settings;

namespace ERP.Application.Pos;

public static class PosPermissions {
    public const string Sell     = "pos.sell";
    public const string Return   = "pos.return";
    public const string Shift    = "pos.shift";
    public const string CashMove = "pos.cashmove";
    public const string Reports  = "pos.reports";
    public const string Manage   = "pos.manage";

    public const string SettingsPos = "ayarlar.pos";

    /// <summary>Kasa noktası seçimi: yönetici ya da satış/vardiya yetkisi olan kasiyer kasaları görebilir.</summary>
    public const string ViewRegisters = "pos.manage|pos.sell|pos.shift";

    /// <summary>Vardiya listesi ve raporu: kasiyer, yönetici ya da rapor yetkisi.</summary>
    public const string ViewShifts = "pos.shift|pos.manage|pos.reports";

    /// <summary>Fiş sorgusu: satış, iade ya da rapor yetkisi olan görebilir.</summary>
    public const string ViewSales = "pos.manage|pos.sell|pos.return|pos.reports";

    /// <summary>"pos" modülü anahtarı (kimlik servisindeki modül listesiyle aynı).</summary>
    public const string Module = "pos";
}

public static class PosErrors {
    public const string RetailCustomerCode = "PERAKENDE";

    public static AppException Conflict(string code, string message) {
        return new AppException(409, code, message);
    }

    public static AppException Bad(string code, string message) {
        return new AppException(400, code, message);
    }

    public static AppException EBelgeRequired() {
        return Conflict("ebelge_required", "e-Arşiv fatura modu için e-Belge kullanımı açık olmalıdır.");
    }

    public static AppException RegisterInactive() {
        return Conflict("register_inactive", "Kasa noktası pasif.");
    }

    public static AppException VersionConflict() {
        return Conflict("version_conflict", "Kayıt başkası tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin.");
    }

    public static AppException ScaleBarcodeInvalid() {
        return Bad("scale_barcode_invalid", "Terazi barkodu geçersiz (kontrol hanesi hatalı).");
    }

    public static bool IsUniqueViolation(DbUpdateException ex) {
        var inner = ex.InnerException;
        return inner?.GetType().Name == "PostgresException"
               && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";
    }
}

/// <summary>
///     POS ayarlarının kurulumu: ilk erişimde "Perakende Müşteri" carisini açar ve ayara yazar. Okuma yolları (ayar ekranı, kasa
///     sorguları) bunu çağırır; aynı anda iki istek gelirse tekil kısıt kazananı belirler ve diğeri sonucu yeniden okur.
/// </summary>
public sealed class PosSetup(
    IApplicationDbContext db,
    TimeProvider          time,
    ICurrentTenant        tenant
) {
    public async Task<SettingsDto> EnsureAsync(CancellationToken ct) {
        for (var attempt = 0; ; attempt++) {
            var row      = await db.TenantSettings.FirstOrDefaultAsync(s => s.Category == PosSettings.Key, ct);
            var settings = row is null ? new PosSettings() : JsonSerializer.Deserialize<PosSettings>(row.Json, SettingsRegistry.Json) ?? new PosSettings();

            if (settings.RetailCustomerId is { } id && await db.CurrentAccounts.AnyAsync(c => c.Id == id, ct))
                return new SettingsDto(row?.Version ?? 0, PosSettings.Normalize(settings));

            var cari = await db.CurrentAccounts.FirstOrDefaultAsync(c => c.Code == PosErrors.RetailCustomerCode, ct);
            if (cari is null) {
                cari = new CurrentAccount {
                    Code = PosErrors.RetailCustomerCode, Name = "Perakende Müşteri", Kind = AccountKind.Individual,
                    IsCustomer = true, IsActive = true, Currency = "TRY", DefaultDueDays = 0
                };
                db.CurrentAccounts.Add(cari);
            } else if (!cari.IsCustomer) {
                cari.IsCustomer = true;
            }

            var updated = PosSettings.Normalize(settings with { RetailCustomerId = cari.Id });
            var now     = time.GetUtcNow();
            if (row is null) {
                row = new TenantSetting { Category = PosSettings.Key, Version = 1 };
                db.TenantSettings.Add(row);
            } else {
                row.Version++;
            }

            row.Json      = JsonSerializer.Serialize(updated, SettingsRegistry.Json);
            row.UpdatedAt = now;
            row.UpdatedBy = tenant.UserFullName ?? tenant.UserId?.ToString() ?? "-";

            try {
                await db.SaveChangesAsync(ct);
                return new SettingsDto(row.Version, updated);
            } catch (Exception ex) when (attempt < 2 && (ex is DbUpdateConcurrencyException
                                                         || ex is DbUpdateException u && PosErrors.IsUniqueViolation(u))) {
                // Aynı anda başka bir istek kurulumu yaptı: durumu yeniden okuyup devam et.
                db.ClearTracking();
            }
        }
    }

    /// <summary>Perakende müşteri carisinin kimliği (gerekirse kurulumu yapar).</summary>
    public async Task<Guid> RetailCustomerIdAsync(CancellationToken ct) {
        var dto = await EnsureAsync(ct);
        return ((PosSettings)dto.Value).RetailCustomerId!.Value;
    }
}
