using ERP.Application.Catalog.Brands;
using ERP.Application.Catalog.Categories;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Catalog : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/catalog";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(ListCategories, "categories");
        groupBuilder.MapPost(CreateCategory, "categories");
        groupBuilder.MapPut(UpdateCategory, "categories/{id:guid}");
        groupBuilder.MapDelete(DeleteCategory, "categories/{id:guid}");
        groupBuilder.MapGet(ListBrands, "brands");
        groupBuilder.MapPost(CreateBrand, "brands");
        groupBuilder.MapPut(UpdateBrand, "brands/{id:guid}");
        groupBuilder.MapDelete(DeleteBrand, "brands/{id:guid}");
    }

    [EndpointSummary("Kategori ağacı")]
    [EndpointDescription("Kök kategorilerden başlayan ağaç. productCount kategoriye doğrudan bağlı ürün sayısıdır.")]
    public static async Task<Ok<IReadOnlyList<CategoryNode>>> ListCategories(ISender sender) {
        return TypedResults.Ok(await sender.Send(new ListCategoriesQuery()));
    }

    [EndpointSummary("Kategori oluştur")]
    [EndpointDescription("En fazla 5 seviye; aynı üst kategoride aynı ad 409 name_taken.")]
    public static async Task<Created<CategoryNode>> CreateCategory(ISender sender, CreateCategoryCommand command) {
        var node = await sender.Send(command);
        return TypedResults.Created($"/api/erp/catalog/categories/{node.Id}", node);
    }

    [EndpointSummary("Kategoriyi güncelle veya taşı")]
    [EndpointDescription("Kendi alt kategorisinin altına taşımak 400 category_cycle, 5 seviyeyi aşmak 400 category_too_deep.")]
    public static async Task<Ok<CategoryNode>> UpdateCategory(ISender sender, Guid id, UpdateCategoryCommand command) {
        return TypedResults.Ok(await sender.Send(command with { Id = id }));
    }

    [EndpointSummary("Kategoriyi sil")]
    [EndpointDescription("Alt kategorisi varsa 409 category_has_children, ürün bağlıysa 409 category_in_use.")]
    public static async Task<NoContent> DeleteCategory(ISender sender, Guid id) {
        await sender.Send(new DeleteCategoryCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Marka listesi")]
    public static async Task<Ok<IReadOnlyList<BrandDto>>> ListBrands(ISender sender) {
        return TypedResults.Ok(await sender.Send(new ListBrandsQuery()));
    }

    [EndpointSummary("Marka oluştur")]
    [EndpointDescription("Aynı ad 409 name_taken.")]
    public static async Task<Created<BrandDto>> CreateBrand(ISender sender, CreateBrandCommand command) {
        var brand = await sender.Send(command);
        return TypedResults.Created($"/api/erp/catalog/brands/{brand.Id}", brand);
    }

    [EndpointSummary("Markayı güncelle")]
    public static async Task<Ok<BrandDto>> UpdateBrand(ISender sender, Guid id, UpdateBrandCommand command) {
        return TypedResults.Ok(await sender.Send(command with { Id = id }));
    }

    [EndpointSummary("Markayı sil")]
    [EndpointDescription("Ürün bağlıysa 409 brand_in_use.")]
    public static async Task<NoContent> DeleteBrand(ISender sender, Guid id) {
        await sender.Send(new DeleteBrandCommand(id));
        return TypedResults.NoContent();
    }
}
