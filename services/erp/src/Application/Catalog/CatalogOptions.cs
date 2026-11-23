using ERP.Application.Catalog.CustomFields;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;

namespace ERP.Application.Catalog;

/// <summary>
///     Ürün formunun ihtiyacı olan seçenekler: birim listesi, varsayılan KDV ve özel alan tanımları. Kod şablonu,
///     barkod öneki ve sayaçlar bilerek yoktur; onlar yalnızca ayarlar.katalog iznine açıktır.
/// </summary>
public sealed record CatalogOptionsDto(string[] Units, int DefaultVatRate, IReadOnlyList<CustomFieldDto> CustomFields);

[Authorize(Permission = CatalogPermissions.Read)]
public sealed record GetCatalogOptionsQuery : IRequest<CatalogOptionsDto>;

public sealed class GetCatalogOptionsQueryHandler(
    IApplicationDbContext db,
    ITenantSettings       settingsService
) : IRequestHandler<GetCatalogOptionsQuery, CatalogOptionsDto> {
    public async Task<CatalogOptionsDto> Handle(GetCatalogOptionsQuery request, CancellationToken ct) {
        var settings = await settingsService.GetAsync<CatalogSettings>(ct);
        var fields = await db.CustomFieldDefinitions.AsNoTracking().OrderBy(d => d.Sort).ThenBy(d => d.Key)
                         .ToListAsync(ct);
        return new CatalogOptionsDto(settings.Units, settings.DefaultVatRate,
                                     fields.Select(CustomFieldRules.ToDto).ToList());
    }
}
