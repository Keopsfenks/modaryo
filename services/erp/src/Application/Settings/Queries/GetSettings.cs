using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.Settings.Queries;

/// <summary>Kategorinin kendi izni (ör. ayarlar.firma) işleyicide denetlenir.</summary>
[Authorize]
public sealed record GetSettingsQuery(
    string Category
) : IRequest<SettingsDto>;

public sealed class GetSettingsQueryHandler(
    IApplicationDbContext db,
    ICurrentSession       session
) : IRequestHandler<GetSettingsQuery, SettingsDto> {
    public async Task<SettingsDto> Handle(GetSettingsQuery request, CancellationToken ct) {
        var info = SettingsSupport.Resolve(request.Category, session);
        var row  = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Category == info.Key, ct);
        if (row is null) return new SettingsDto(0, info.CreateDefault());

        using var doc = System.Text.Json.JsonDocument.Parse(row.Json);
        return new SettingsDto(row.Version, info.Deserialize(doc.RootElement));
    }
}
