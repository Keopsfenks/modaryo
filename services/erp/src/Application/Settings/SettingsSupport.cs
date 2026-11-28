using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Settings.Categories;

namespace ERP.Application.Settings;

public sealed record SettingsDto(int Version, object Value);

internal static class SettingsSupport {
    /// <summary>Kategoriyi bulur (yoksa 404) ve kategorinin iznini aktif firmada denetler (yoksa 403).</summary>
    public static SettingsCategoryInfo Resolve(string category, ICurrentSession session) {
        var info = SettingsRegistry.Find(category)
                   ?? throw new NotFoundException(category, "SettingsCategory");
        if (session.TenantId is null || !session.HasPermission(info.Permission))
            throw new ForbiddenAccessException();
        if (info.Key == PosSettings.Key && !session.HasModule(ERP.Application.Pos.PosPermissions.Module))
            throw new AppException(403, "module_disabled", "Bu modül firmanızda kapalı.");

        return info;
    }
}

/// <summary>Başka ERP kodunun kullandığı tip güvenli okuma; izin denetimi yapmaz, kayıt yoksa varsayılanları döner.</summary>
public sealed class TenantSettingsService(
    IApplicationDbContext db
) : ITenantSettings {
    public async Task<T> GetAsync<T>(CancellationToken ct) where T : class, ISettingsCategory<T>, new() {
        var key = T.Key;
        var json = await db.TenantSettings.AsNoTracking().Where(s => s.Category == key).Select(s => s.Json)
                       .FirstOrDefaultAsync(ct);
        return json is null
                   ? new T()
                   : System.Text.Json.JsonSerializer.Deserialize<T>(json, SettingsRegistry.Json) ?? new T();
    }
}
