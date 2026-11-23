using System.Linq.Expressions;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Catalog;

namespace ERP.Application.Catalog.Brands;

public sealed record BrandDto(Guid Id, string Name, int ProductCount);

public static class BrandErrors {
    public static AppException NameTaken() {
        return new AppException(409, "name_taken", "Bu adlı bir marka zaten var.");
    }

    public static AppException InUse() {
        return new AppException(409, "brand_in_use",
                                "Ürün bağlı marka silinemez. Önce ürünlerin markasını değiştirin.");
    }

    public static bool IsUniqueViolation(DbUpdateException ex) {
        return ex.InnerException?.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException) as string == "23505";
    }

    public static void AddNameRule<T>(this AbstractValidator<T> v, Expression<Func<T, string>> name) {
        v.RuleFor(name).NotEmpty().WithMessage("Marka adı zorunludur.")
            .Must(n => n is null || n.Trim().Length <= 100).WithMessage("Marka adı en fazla 100 karakter olabilir.");
    }
}

[Authorize(Permission = CatalogPermissions.Read)]
public sealed record ListBrandsQuery : IRequest<IReadOnlyList<BrandDto>>;

public sealed class ListBrandsQueryHandler(
    IApplicationDbContext db
) : IRequestHandler<ListBrandsQuery, IReadOnlyList<BrandDto>> {
    public async Task<IReadOnlyList<BrandDto>> Handle(ListBrandsQuery request, CancellationToken ct) {
        var counts = await db.Products.AsNoTracking().Where(p => p.BrandId != null)
                         .GroupBy(p => p.BrandId!.Value).Select(g => new { Id = g.Key, Count = g.Count() })
                         .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var brands = await db.Brands.AsNoTracking().OrderBy(b => b.Name).ToListAsync(ct);
        return brands.Select(b => new BrandDto(b.Id, b.Name, counts.GetValueOrDefault(b.Id))).ToList();
    }
}

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record CreateBrandCommand(string Name) : IRequest<BrandDto>;

public sealed class CreateBrandCommandValidator : AbstractValidator<CreateBrandCommand> {
    public CreateBrandCommandValidator() {
        this.AddNameRule(c => c.Name);
    }
}

public sealed class CreateBrandCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<CreateBrandCommand, BrandDto> {
    public async Task<BrandDto> Handle(CreateBrandCommand request, CancellationToken ct) {
        var name = request.Name.Trim();
        if (await db.Brands.AnyAsync(b => b.Name == name, ct)) throw BrandErrors.NameTaken();

        var brand = new Brand { Name = name };
        db.Brands.Add(brand);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (BrandErrors.IsUniqueViolation(ex)) {
            throw BrandErrors.NameTaken();
        }

        return new BrandDto(brand.Id, brand.Name, 0);
    }
}

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record UpdateBrandCommand(Guid Id, string Name) : IRequest<BrandDto>;

public sealed class UpdateBrandCommandValidator : AbstractValidator<UpdateBrandCommand> {
    public UpdateBrandCommandValidator() {
        this.AddNameRule(c => c.Name);
    }
}

public sealed class UpdateBrandCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<UpdateBrandCommand, BrandDto> {
    public async Task<BrandDto> Handle(UpdateBrandCommand request, CancellationToken ct) {
        var name  = request.Name.Trim();
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == request.Id, ct)
                    ?? throw new NotFoundException(request.Id.ToString(), "Brand");
        if (await db.Brands.AnyAsync(b => b.Id != brand.Id && b.Name == name, ct)) throw BrandErrors.NameTaken();

        brand.Name = name;
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (BrandErrors.IsUniqueViolation(ex)) {
            throw BrandErrors.NameTaken();
        }

        var count = await db.Products.CountAsync(p => p.BrandId == brand.Id, ct);
        return new BrandDto(brand.Id, brand.Name, count);
    }
}

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record DeleteBrandCommand(Guid Id) : IRequest;

public sealed class DeleteBrandCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<DeleteBrandCommand> {
    public async Task Handle(DeleteBrandCommand request, CancellationToken ct) {
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == request.Id, ct)
                    ?? throw new NotFoundException(request.Id.ToString(), "Brand");
        if (await db.Products.AnyAsync(p => p.BrandId == brand.Id, ct)) throw BrandErrors.InUse();

        db.Brands.Remove(brand);
        await db.SaveChangesAsync(ct);
    }
}
