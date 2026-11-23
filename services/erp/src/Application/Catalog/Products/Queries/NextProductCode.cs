using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;

namespace ERP.Application.Catalog.Products.Queries;

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record NextProductCodeQuery : IRequest<NextCodeResult>;

/// <summary>Öneri verir, sayacı tüketmez: kart kaydedilince sayaç ilerler.</summary>
public sealed class NextProductCodeQueryHandler(
    IApplicationDbContext db,
    ITenantSettings       settingsService
) : IRequestHandler<NextProductCodeQuery, NextCodeResult> {
    public async Task<NextCodeResult> Handle(NextProductCodeQuery request, CancellationToken ct) {
        var settings = await settingsService.GetAsync<CatalogSettings>(ct);
        var next     = await ProductCodes.NextFreeSequenceAsync(db, settings, ct);
        return new NextCodeResult(ProductCodes.Format(settings.CodeTemplate, next));
    }
}
