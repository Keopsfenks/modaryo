using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.Catalog.Variants.Commands;

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record SetVariantActiveCommand(Guid ProductId, Guid VariantId, bool Active) : IRequest;

public sealed class SetVariantActiveCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<SetVariantActiveCommand> {
    public async Task Handle(SetVariantActiveCommand request, CancellationToken ct) {
        var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == request.VariantId
                                                                         && v.ProductId == request.ProductId, ct)
                      ?? throw new NotFoundException(request.VariantId.ToString(), "ProductVariant");
        variant.IsActive = request.Active;
        await db.SaveChangesAsync(ct);
    }
}
