using ERP.Application.Catalog.Brands;
using ERP.Application.Catalog.Categories;
using ERP.Application.Catalog.CustomFields;
using ERP.Application.Catalog.Products;
using ERP.Application.Catalog.Products.Commands;
using ERP.Application.Catalog.Products.Queries;
using ERP.Application.Catalog.Items;
using ERP.Application.Catalog.Items.Queries;
using ERP.Application.Catalog.Variants.Commands;
using ERP.Application.CurrentAccounts;
using ERP.Domain.Catalog;
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
        groupBuilder.MapGet(ListProducts, "products");
        groupBuilder.MapGet(NextProductCode, "products/next-code");
        groupBuilder.MapGet(GetProduct, "products/{id:guid}");
        groupBuilder.MapPost(CreateProduct, "products");
        groupBuilder.MapPut(UpdateProduct, "products/{id:guid}");
        groupBuilder.MapPost(ActivateProduct, "products/{id:guid}/activate");
        groupBuilder.MapPost(DeactivateProduct, "products/{id:guid}/deactivate");
        groupBuilder.MapDelete(DeleteProduct, "products/{id:guid}");
        groupBuilder.MapPost(GenerateBarcode, "products/{id:guid}/barcode/generate");
        groupBuilder.MapPut(SetAttributes, "products/{id:guid}/attributes");
        groupBuilder.MapPut(UpdateVariant, "products/{id:guid}/variants/{variantId:guid}");
        groupBuilder.MapPost(ActivateVariant, "products/{id:guid}/variants/{variantId:guid}/activate");
        groupBuilder.MapPost(DeactivateVariant, "products/{id:guid}/variants/{variantId:guid}/deactivate");
        groupBuilder.MapGet(SearchItems, "items");
        groupBuilder.MapGet(LookupBarcode, "items/barcode/{value}");
        groupBuilder.MapGet(ListCustomFields, "custom-fields");
        groupBuilder.MapPost(CreateCustomField, "custom-fields");
        groupBuilder.MapPut(UpdateCustomField, "custom-fields/{id:guid}");
        groupBuilder.MapDelete(DeleteCustomField, "custom-fields/{id:guid}");
    }

    [EndpointSummary("Ürün ve hizmet listesi")]
    [EndpointDescription("search: ad, kod, barkod veya tedarikçi kodu (Türkçe harf katlamalı). kind: product | service. categoryId alt kategorileri de kapsar. Pasifler yalnızca includeInactive=true ile gelir. pageSize varsayılan 25, en fazla 100.")]
    public static async Task<Ok<PagedResult<ProductListItem>>> ListProducts(
        ISender      sender,
        string?      search,
        string?      kind,
        Guid?        categoryId,
        Guid?        brandId,
        string?      tag,
        bool         includeInactive = false,
        int          page            = 1,
        int          pageSize        = 25
    ) {
        return TypedResults.Ok(await sender.Send(new ListProductsQuery(search, kind, categoryId, brandId, tag,
                                                                        includeInactive, page, pageSize)));
    }

    [EndpointSummary("Sonraki ürün kodu")]
    [EndpointDescription("Ayarlardaki şablona göre önerilen kod; sayacı tüketmez.")]
    public static async Task<Ok<NextCodeResult>> NextProductCode(ISender sender) {
        return TypedResults.Ok(await sender.Send(new NextProductCodeQuery()));
    }

    [EndpointSummary("Ürün veya hizmet kartı")]
    public static async Task<Ok<ProductDto>> GetProduct(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetProductQuery(id)));
    }

    [EndpointSummary("Ürün veya hizmet oluştur")]
    [EndpointDescription("code boşsa şablondan verilir. Kod çakışırsa 409 code_taken, barkod çakışırsa 409 barcode_taken, geçersiz birim çevrimi 400 invalid_conversion. Hizmette varyant, seri/lot ve ölçüyle satış olamaz. Seri/lot için seri-lot modülü gerekir (403 module_disabled). attributes ve variants yalnızca oluştururken kullanılır; sonra ayrı uçlarla yönetilir.")]
    public static async Task<Created<ProductDto>> CreateProduct(ISender sender, CreateProductCommand command) {
        var product = await sender.Send(command);
        return TypedResults.Created($"/api/erp/catalog/products/{product.Id}", product);
    }

    [EndpointSummary("Ürün veya hizmeti güncelle")]
    [EndpointDescription("Kart alanları, birimler, tedarikçiler, görseller ve özel alanlar bütün olarak değiştirilir. Varyantlara dokunulmaz. Kart türü değişmez.")]
    public static async Task<Ok<ProductDto>> UpdateProduct(ISender sender, Guid id, UpdateProductCommand command) {
        return TypedResults.Ok(await sender.Send(command with { Id = id }));
    }

    [EndpointSummary("Ürünü aktifleştir")]
    public static async Task<NoContent> ActivateProduct(ISender sender, Guid id) {
        await sender.Send(new SetProductActiveCommand(id, true));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Ürünü pasifleştir")]
    public static async Task<NoContent> DeactivateProduct(ISender sender, Guid id) {
        await sender.Send(new SetProductActiveCommand(id, false));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Ürünü sil")]
    [EndpointDescription("Başka bir modülde kullanımdaysa 409 product_in_use; bunun yerine pasifleştirin.")]
    public static async Task<NoContent> DeleteProduct(ISender sender, Guid id) {
        await sender.Send(new DeleteProductCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("EAN-13 barkod üret")]
    [EndpointDescription("Ayarlardaki önek ve sıra sayacından kullanılmayan bir barkod döndürür; karta yazmaz.")]
    public static async Task<Ok<BarcodeResult>> GenerateBarcode(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GenerateBarcodeCommand(id)));
    }

    [EndpointSummary("Varyant özelliklerini ayarla")]
    [EndpointDescription("Kombinasyonları yeniden üretir. Değer kümesi korunan varyantlar (kimlik, kod, barkod) değişmez; yeni kombinasyonlar {ürünKodu}-{n} koduyla eklenir. Kaldırılan değerin varyantı kullanımdaysa 409 variant_in_use; kullanılmayanlar silinir. 500'ü aşarsa 409 too_many_variants.")]
    public static async Task<Ok<ProductDto>> SetAttributes(ISender sender, Guid id, SetAttributesBody body) {
        return TypedResults.Ok(await sender.Send(new SetAttributesCommand(id, body.Attributes)));
    }

    [EndpointSummary("Varyantı güncelle")]
    [EndpointDescription("Kod, barkod ve fiyat geçersiz kılmaları bütün olarak değiştirilir. Çakışmada 409 code_taken veya barcode_taken.")]
    public static async Task<Ok<ProductDto>> UpdateVariant(ISender sender, Guid id, Guid variantId, UpdateVariantCommand command) {
        return TypedResults.Ok(await sender.Send(command with { ProductId = id, VariantId = variantId }));
    }

    [EndpointSummary("Varyantı aktifleştir")]
    public static async Task<NoContent> ActivateVariant(ISender sender, Guid id, Guid variantId) {
        await sender.Send(new SetVariantActiveCommand(id, variantId, true));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Varyantı pasifleştir")]
    public static async Task<NoContent> DeactivateVariant(ISender sender, Guid id, Guid variantId) {
        await sender.Send(new SetVariantActiveCommand(id, variantId, false));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Satılabilir kalem ara")]
    [EndpointDescription("Varyantsız kart tek kalem, varyantlı kart varyant başına bir kalemdir. search: kod, ad, barkod veya tedarikçi kodu (Türkçe harf katlamalı). kind: product | service. Pasifler yalnızca includeInactive=true ile gelir. limit varsayılan 20, en fazla 50.")]
    public static async Task<Ok<IReadOnlyList<SellableItem>>> SearchItems(
        ISender sender,
        string? search,
        string? kind,
        bool    includeInactive = false,
        int     limit           = 20
    ) {
        return TypedResults.Ok(await sender.Send(new SearchItemsQuery(search, kind, includeInactive, limit)));
    }

    [EndpointSummary("Barkoddan kalem bul")]
    [EndpointDescription("Birim barkoduysa o birim ve çevrim katsayısı, değilse temel birim ve 1 döner. Bulunamazsa 404.")]
    public static async Task<Ok<BarcodeHit>> LookupBarcode(ISender sender, string value) {
        return TypedResults.Ok(await sender.Send(new LookupBarcodeQuery(value)));
    }

    [EndpointSummary("Özel alan tanımları")]
    [EndpointDescription("katalog.read veya ayarlar.katalog ile okunur.")]
    public static async Task<Ok<IReadOnlyList<CustomFieldDto>>> ListCustomFields(ISender sender) {
        return TypedResults.Ok(await sender.Send(new ListCustomFieldsQuery()));
    }

    [EndpointSummary("Özel alan tanımla")]
    [EndpointDescription("type: text | number | date | select | bool. Anahtar küçük harf/rakam/alt çizgi; aynı anahtar 409 key_taken; en fazla 30 tanım.")]
    public static async Task<Created<CustomFieldDto>> CreateCustomField(ISender sender, CreateCustomFieldCommand command) {
        var field = await sender.Send(command);
        return TypedResults.Created($"/api/erp/catalog/custom-fields/{field.Id}", field);
    }

    [EndpointSummary("Özel alanı güncelle")]
    [EndpointDescription("Anahtar ve tür değişmez.")]
    public static async Task<Ok<CustomFieldDto>> UpdateCustomField(ISender sender, Guid id, UpdateCustomFieldCommand command) {
        return TypedResults.Ok(await sender.Send(command with { Id = id }));
    }

    [EndpointSummary("Özel alanı sil")]
    public static async Task<NoContent> DeleteCustomField(ISender sender, Guid id) {
        await sender.Send(new DeleteCustomFieldCommand(id));
        return TypedResults.NoContent();
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

public sealed record SetAttributesBody(List<AttributeValuesInput>? Attributes);
