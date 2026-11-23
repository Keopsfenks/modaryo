using System.Linq.Expressions;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Catalog;

namespace ERP.Application.Catalog.Categories;

public sealed record CategoryNode(
    Guid                        Id,
    string                      Name,
    Guid?                       ParentId,
    int                         Sort,
    int                         ProductCount,
    IReadOnlyList<CategoryNode> Children
);

public static class CategoryErrors {
    public const int MaxDepth = 5;

    public static AppException NameTaken() {
        return new AppException(409, "name_taken", "Bu üst kategoride aynı adlı bir kategori zaten var.");
    }

    public static AppException HasChildren() {
        return new AppException(409, "category_has_children", "Alt kategorisi olan kategori silinemez.");
    }

    public static AppException InUse() {
        return new AppException(409, "category_in_use",
                                "Ürün bağlı kategori silinemez. Önce ürünlerin kategorisini değiştirin.");
    }

    public static AppException TooDeep() {
        return new AppException(400, "category_too_deep", "Kategori ağacı en fazla 5 seviye olabilir.");
    }

    public static AppException Cycle() {
        return new AppException(400, "category_cycle", "Kategori kendi alt kategorisinin altına taşınamaz.");
    }

    public static bool IsUniqueViolation(DbUpdateException ex) {
        return ex.InnerException?.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException) as string == "23505";
    }
}

[Authorize(Permission = CatalogPermissions.Read)]
public sealed record ListCategoriesQuery : IRequest<IReadOnlyList<CategoryNode>>;

public sealed class ListCategoriesQueryHandler(
    IApplicationDbContext db
) : IRequestHandler<ListCategoriesQuery, IReadOnlyList<CategoryNode>> {
    public async Task<IReadOnlyList<CategoryNode>> Handle(ListCategoriesQuery request, CancellationToken ct) {
        var all = await db.Categories.AsNoTracking().ToListAsync(ct);
        var counts = await db.Products.AsNoTracking().Where(p => p.CategoryId != null)
                         .GroupBy(p => p.CategoryId!.Value).Select(g => new { Id = g.Key, Count = g.Count() })
                         .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var byParent = all.ToLookup(c => c.ParentId);

        IReadOnlyList<CategoryNode> Build(Guid? parent) {
            return byParent[parent].OrderBy(c => c.Sort).ThenBy(c => c.Name, StringComparer.CurrentCulture)
                .Select(c => new CategoryNode(c.Id, c.Name, c.ParentId, c.Sort, counts.GetValueOrDefault(c.Id),
                                              Build(c.Id)))
                .ToList();
        }

        return Build(null);
    }
}

public static class CategoryRules {
    public static void AddNameRule<T>(this AbstractValidator<T> v, Expression<Func<T, string>> name) {
        v.RuleFor(name).NotEmpty().WithMessage("Kategori adı zorunludur.")
            .Must(n => n is null || n.Trim().Length <= 100).WithMessage("Kategori adı en fazla 100 karakter olabilir.");
    }

    /// <summary>Kategorinin kökten itibaren derinliği (kök = 1).</summary>
    public static int DepthOf(Guid? id, Dictionary<Guid, Category> all) {
        var depth   = 0;
        var visited = new HashSet<Guid>();
        while (id is { } current && visited.Add(current) && all.TryGetValue(current, out var node)) {
            depth++;
            id = node.ParentId;
        }

        return depth;
    }

    /// <summary>Alt ağaçtaki seviye sayısı (kendisi dahil).</summary>
    public static int Height(Guid id, ILookup<Guid?, Category> byParent) {
        var children = byParent[id].ToList();
        return 1 + (children.Count == 0 ? 0 : children.Max(c => Height(c.Id, byParent)));
    }
}

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record CreateCategoryCommand(string Name, Guid? ParentId) : IRequest<CategoryNode>;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand> {
    public CreateCategoryCommandValidator() {
        this.AddNameRule(c => c.Name);
    }
}

public sealed class CreateCategoryCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<CreateCategoryCommand, CategoryNode> {
    public async Task<CategoryNode> Handle(CreateCategoryCommand request, CancellationToken ct) {
        var name = request.Name.Trim();
        var all  = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);

        if (request.ParentId is { } parentId) {
            if (!all.ContainsKey(parentId)) throw new NotFoundException(parentId.ToString(), "Category");
            if (CategoryRules.DepthOf(parentId, all) >= CategoryErrors.MaxDepth) throw CategoryErrors.TooDeep();
        }

        if (all.Values.Any(c => c.ParentId == request.ParentId && c.Name == name)) throw CategoryErrors.NameTaken();

        var sort = all.Values.Where(c => c.ParentId == request.ParentId).Select(c => c.Sort).DefaultIfEmpty(-1).Max() + 1;
        var category = new Category { Name = name, ParentId = request.ParentId, Sort = sort };
        db.Categories.Add(category);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (CategoryErrors.IsUniqueViolation(ex)) {
            throw CategoryErrors.NameTaken();
        }

        return new CategoryNode(category.Id, category.Name, category.ParentId, category.Sort, 0, []);
    }
}

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record UpdateCategoryCommand(Guid Id, string Name, Guid? ParentId, int Sort) : IRequest<CategoryNode>;

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand> {
    public UpdateCategoryCommandValidator() {
        this.AddNameRule(c => c.Name);
        RuleFor(c => c.Sort).InclusiveBetween(0, 100000).WithMessage("Sıra 0 ile 100000 arasında olmalıdır.");
    }
}

public sealed class UpdateCategoryCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<UpdateCategoryCommand, CategoryNode> {
    public async Task<CategoryNode> Handle(UpdateCategoryCommand request, CancellationToken ct) {
        var name = request.Name.Trim();
        var all  = await db.Categories.ToDictionaryAsync(c => c.Id, ct);
        if (!all.TryGetValue(request.Id, out var category))
            throw new NotFoundException(request.Id.ToString(), "Category");

        if (request.ParentId is { } parentId) {
            if (!all.ContainsKey(parentId)) throw new NotFoundException(parentId.ToString(), "Category");

            // Kendisi ya da alt ağacındaki bir kategorinin altına taşınamaz.
            for (Guid? walk = parentId; walk is { } w; walk = all.GetValueOrDefault(w)?.ParentId)
                if (w == category.Id)
                    throw CategoryErrors.Cycle();

            var byParent = all.Values.ToLookup(c => c.ParentId);
            if (CategoryRules.DepthOf(parentId, all) + CategoryRules.Height(category.Id, byParent)
                > CategoryErrors.MaxDepth)
                throw CategoryErrors.TooDeep();
        }

        if (all.Values.Any(c => c.Id != category.Id && c.ParentId == request.ParentId && c.Name == name))
            throw CategoryErrors.NameTaken();

        category.Name     = name;
        category.ParentId = request.ParentId;
        category.Sort     = request.Sort;
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (CategoryErrors.IsUniqueViolation(ex)) {
            throw CategoryErrors.NameTaken();
        }

        var count = await db.Products.CountAsync(p => p.CategoryId == category.Id, ct);
        return new CategoryNode(category.Id, category.Name, category.ParentId, category.Sort, count, []);
    }
}

[Authorize(Permission = CatalogPermissions.Manage)]
public sealed record DeleteCategoryCommand(Guid Id) : IRequest;

public sealed class DeleteCategoryCommandHandler(
    IApplicationDbContext db
) : IRequestHandler<DeleteCategoryCommand> {
    public async Task Handle(DeleteCategoryCommand request, CancellationToken ct) {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                       ?? throw new NotFoundException(request.Id.ToString(), "Category");
        if (await db.Categories.AnyAsync(c => c.ParentId == category.Id, ct)) throw CategoryErrors.HasChildren();
        if (await db.Products.AnyAsync(p => p.CategoryId == category.Id, ct)) throw CategoryErrors.InUse();

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }
}
