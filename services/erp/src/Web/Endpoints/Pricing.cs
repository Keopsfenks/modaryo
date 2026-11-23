using ERP.Application.CurrentAccounts;
using ERP.Application.Pricing;
using ERP.Application.Pricing.CustomerPrices;
using ERP.Application.Pricing.Groups;
using ERP.Application.Pricing.History;
using ERP.Application.Pricing.Items;
using ERP.Application.Pricing.Lists;
using ERP.Domain.Pricing;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Pricing : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/pricing";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(ListLists, "lists");
        groupBuilder.MapGet(GetList, "lists/{id:guid}");
        groupBuilder.MapPost(CreateList, "lists");
        groupBuilder.MapPut(UpdateList, "lists/{id:guid}");
        groupBuilder.MapDelete(DeleteList, "lists/{id:guid}");
        groupBuilder.MapGet(ListItems, "lists/{id:guid}/items");
        groupBuilder.MapPut(UpsertItems, "lists/{id:guid}/items");
        groupBuilder.MapDelete(DeleteItem, "lists/{id:guid}/items/{itemId:guid}");
        groupBuilder.MapPost(BulkAdjust, "lists/{id:guid}/bulk-adjust");
        groupBuilder.MapGet(ListGroups, "customer-groups");
        groupBuilder.MapPost(CreateGroup, "customer-groups");
        groupBuilder.MapPut(UpdateGroup, "customer-groups/{id:guid}");
        groupBuilder.MapDelete(DeleteGroup, "customer-groups/{id:guid}");
        groupBuilder.MapGet(ListCustomerPrices, "customers/{currentAccountId:guid}/prices");
        groupBuilder.MapPost(CreateCustomerPrice, "customers/{currentAccountId:guid}/prices");
        groupBuilder.MapPut(UpdateCustomerPrice, "customers/{currentAccountId:guid}/prices/{id:guid}");
        groupBuilder.MapDelete(DeleteCustomerPrice, "customers/{currentAccountId:guid}/prices/{id:guid}");
        groupBuilder.MapGet(ListHistory, "history");
    }

    [EndpointSummary("Fiyat listeleri")]
    [EndpointDescription("fiyat.read. kind: sale | purchase (boş: ikisi). fiyat.alis izni yoksa alış listeleri hiç görünmez. Pasifler yalnızca includeInactive=true ile gelir.")]
    public static async Task<Ok<IReadOnlyList<PriceListDto>>> ListLists(ISender sender, string? kind, bool includeInactive = false) {
        return TypedResults.Ok(await sender.Send(new ListPriceListsQuery(ParseKind(kind), includeInactive)));
    }

    [EndpointSummary("Fiyat listesi")]
    [EndpointDescription("Alış listesi fiyat.alis izni ister (yoksa 403).")]
    public static async Task<Ok<PriceListDto>> GetList(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetPriceListQuery(id)));
    }

    [EndpointSummary("Fiyat listesi oluştur")]
    [EndpointDescription("fiyat.manage; alış listesi için ayrıca fiyat.alis. Türetilmiş liste: baseListId + adjustmentPercent (-100..1000). Döngü 409 list_cycle, 3 düzeyden derin zincir 409 list_depth, tür/para birimi uyumsuzluğu 400.")]
    public static async Task<Created<PriceListDto>> CreateList(ISender sender, CreatePriceListCommand command) {
        var list = await sender.Send(command);
        return TypedResults.Created($"/api/erp/pricing/lists/{list.Id}", list);
    }

    [EndpointSummary("Fiyat listesini güncelle")]
    [EndpointDescription("Tür değiştirilemez. version verilirse eşleşmeyen 409 version_conflict.")]
    public static async Task<Ok<PriceListDto>> UpdateList(ISender sender, Guid id, UpdatePriceListCommand command) {
        return TypedResults.Ok(await sender.Send(command with { Id = id }));
    }

    [EndpointSummary("Fiyat listesini sil")]
    [EndpointDescription("Varsayılan liste, müşteri grubu listesi ya da türetilmiş listenin tabanıysa 409 list_in_use. Satırları birlikte silinir.")]
    public static async Task<NoContent> DeleteList(ISender sender, Guid id) {
        await sender.Send(new DeletePriceListCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Liste satırları")]
    [EndpointDescription("search: ürün adı/kodu. Türetilmiş listede açık satırı olmayan kalemler hesaplanmış kademeler ve derived=true ile gelir.")]
    public static async Task<Ok<PagedResult<PriceItemDto>>> ListItems(ISender sender, Guid id, string? search, int page = 1, int pageSize = 50) {
        return TypedResults.Ok(await sender.Send(new ListPriceItemsQuery(id, search, page, pageSize)));
    }

    [EndpointSummary("Liste satırlarını kaydet")]
    [EndpointDescription("Gövde: { rows: [{ productId, variantId?, unit, tiers[{minQty, price}], version? }] }. Satır kalem+birim anahtarıyla eklenir ya da güncellenir; version verilirse eski sürüm 409 version_conflict. Kademeler geçersizse 400 invalid_tiers. Hepsi tek işlemde yazılır, değişen her satır geçmişe düşer.")]
    public static async Task<Ok<PriceItemsResult>> UpsertItems(ISender sender, Guid id, UpsertItemsBody body) {
        return TypedResults.Ok(await sender.Send(new UpsertPriceItemsCommand(id, body.Rows)));
    }

    [EndpointSummary("Liste satırını sil")]
    public static async Task<NoContent> DeleteItem(ISender sender, Guid id, Guid itemId) {
        await sender.Send(new DeletePriceItemCommand(id, itemId));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Toplu fiyat ayarı")]
    [EndpointDescription("Gövde: { percent, categoryId?, brandId? }. Açık satırların fiyatları (1+yüzde) ile çarpılır, ayarlardaki yuvarlama uygulanır; her satır geçmişe yazılır. Yanıt: { updated }.")]
    public static async Task<Ok<BulkAdjustResult>> BulkAdjust(ISender sender, Guid id, BulkAdjustBody body) {
        return TypedResults.Ok(await sender.Send(new BulkAdjustCommand(id, body.Percent, body.CategoryId, body.BrandId)));
    }

    [EndpointSummary("Müşteri grupları")]
    public static async Task<Ok<IReadOnlyList<CustomerGroupDto>>> ListGroups(ISender sender) {
        return TypedResults.Ok(await sender.Send(new ListCustomerGroupsQuery()));
    }

    [EndpointSummary("Müşteri grubu oluştur")]
    [EndpointDescription("Aynı ad 409 name_taken. salePriceListId bir satış listesi olmalıdır.")]
    public static async Task<Created<CustomerGroupDto>> CreateGroup(ISender sender, CreateCustomerGroupCommand command) {
        var group = await sender.Send(command);
        return TypedResults.Created($"/api/erp/pricing/customer-groups/{group.Id}", group);
    }

    [EndpointSummary("Müşteri grubunu güncelle")]
    public static async Task<Ok<CustomerGroupDto>> UpdateGroup(ISender sender, Guid id, UpdateCustomerGroupCommand command) {
        return TypedResults.Ok(await sender.Send(command with { Id = id }));
    }

    [EndpointSummary("Müşteri grubunu sil")]
    [EndpointDescription("Cari bağlıysa 409 group_in_use.")]
    public static async Task<NoContent> DeleteGroup(ISender sender, Guid id) {
        await sender.Send(new DeleteCustomerGroupCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Müşteriye özel fiyatlar")]
    public static async Task<Ok<IReadOnlyList<CustomerPriceDto>>> ListCustomerPrices(ISender sender, Guid currentAccountId) {
        return TypedResults.Ok(await sender.Send(new ListCustomerPricesQuery(currentAccountId)));
    }

    [EndpointSummary("Müşteriye özel fiyat ekle")]
    [EndpointDescription("Aynı müşteri, kalem, birim ve para biriminde tarihleri çakışan fiyat 409 price_overlap.")]
    public static async Task<Created<CustomerPriceDto>> CreateCustomerPrice(ISender sender, Guid currentAccountId, CreateCustomerPriceCommand command) {
        var price = await sender.Send(command with { CurrentAccountId = currentAccountId });
        return TypedResults.Created($"/api/erp/pricing/customers/{currentAccountId}/prices/{price.Id}", price);
    }

    [EndpointSummary("Müşteriye özel fiyatı güncelle")]
    public static async Task<Ok<CustomerPriceDto>> UpdateCustomerPrice(ISender sender, Guid currentAccountId, Guid id, UpdateCustomerPriceCommand command) {
        return TypedResults.Ok(await sender.Send(command with { CurrentAccountId = currentAccountId, Id = id }));
    }

    [EndpointSummary("Müşteriye özel fiyatı sil")]
    public static async Task<NoContent> DeleteCustomerPrice(ISender sender, Guid currentAccountId, Guid id) {
        await sender.Send(new DeleteCustomerPriceCommand(currentAccountId, id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Fiyat geçmişi")]
    [EndpointDescription("En yeni önce. scope: list | customer | card. Alış türündeki kayıtlar yalnızca fiyat.alis izniyle gelir.")]
    public static async Task<Ok<PagedResult<PriceHistoryDto>>> ListHistory(
        ISender sender, Guid? productId, Guid? variantId, string? scope, int page = 1, int pageSize = 50) {
        return TypedResults.Ok(await sender.Send(new ListPriceHistoryQuery(productId, variantId, scope, page, pageSize)));
    }

    internal static PriceListKind? ParseKind(string? kind) {
        return Enum.TryParse<PriceListKind>(kind, true, out var parsed) ? parsed : null;
    }
}

public sealed record UpsertItemsBody(List<PriceRowInput>? Rows);

public sealed record BulkAdjustBody(decimal Percent, Guid? CategoryId, Guid? BrandId);
