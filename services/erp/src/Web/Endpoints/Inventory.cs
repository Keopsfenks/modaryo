using ERP.Application.Inventory.Tracking.Queries;
using ERP.Application.Inventory.Warehouses;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Inventory : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/inventory";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(ListWarehouses, "warehouses");
        groupBuilder.MapGet(GetDefaultWarehouse, "warehouses/default");
        groupBuilder.MapPost(CreateWarehouse, "warehouses");
        groupBuilder.MapPut(UpdateWarehouse, "warehouses/{id:guid}");
        groupBuilder.MapPost(ActivateWarehouse, "warehouses/{id:guid}/activate");
        groupBuilder.MapPost(DeactivateWarehouse, "warehouses/{id:guid}/deactivate");
        groupBuilder.MapDelete(DeleteWarehouse, "warehouses/{id:guid}");
        groupBuilder.MapGet(ListLots, "tracking/lots");
        groupBuilder.MapGet(SuggestLots, "tracking/lots/suggest");
        groupBuilder.MapGet(ListSerials, "tracking/serials");
    }

    [EndpointSummary("Lotlar")]
    [EndpointDescription("stok.read, seri-lot modülü (kapalıysa 403 module_disabled). Stoğu olan lotlar son kullanma tarihine göre; warehouseId verilmezse erişilebilir depoların toplamı.")]
    public static async Task<Ok<IReadOnlyList<LotDto>>> ListLots(ISender sender, Guid productId, Guid? variantId, Guid? warehouseId) {
        return TypedResults.Ok(await sender.Send(new ListLotsQuery(productId, variantId, warehouseId)));
    }

    [EndpointSummary("FEFO lot önerisi")]
    [EndpointDescription("stok.read, seri-lot modülü. Son kullanma tarihi en yakın lottan başlayan çıkış önerisi; stok yetmezse eldeki kadarı döner.")]
    public static async Task<Ok<IReadOnlyList<LotPickDto>>> SuggestLots(ISender sender, Guid productId, Guid? variantId, Guid warehouseId, decimal qty) {
        return TypedResults.Ok(await sender.Send(new SuggestLotsQuery(productId, variantId, warehouseId, qty)));
    }

    [EndpointSummary("Seri numaraları")]
    [EndpointDescription("stok.read, seri-lot modülü. inStock true: stokta olanlar, false: olmayanlar.")]
    public static async Task<Ok<IReadOnlyList<SerialDto>>> ListSerials(ISender sender, Guid productId, Guid? variantId, Guid? warehouseId, bool? inStock) {
        return TypedResults.Ok(await sender.Send(new ListSerialsQuery(productId, variantId, warehouseId, inStock)));
    }

    [EndpointSummary("Depolar")]
    [EndpointDescription("stok.read. Yalnızca oturumun erişebildiği şubelerin depoları gelir; inUse hareketi/sayımı olan depodur.")]
    public static async Task<Ok<IReadOnlyList<WarehouseDto>>> ListWarehouses(ISender sender) {
        return TypedResults.Ok(await sender.Send(new ListWarehousesQuery()));
    }

    [EndpointSummary("Şubenin varsayılan deposu")]
    [EndpointDescription("stok.read. Yoksa \"{şube adı} Depo\" adıyla Normal depo oluşturulur. Erişilemeyen şube 403 branch_forbidden, bilinmeyen ya da pasif şube 404 branch_not_found.")]
    public static async Task<Ok<WarehouseDto>> GetDefaultWarehouse(ISender sender, Guid branchId) {
        return TypedResults.Ok(await sender.Send(new GetDefaultWarehouseQuery(branchId)));
    }

    [EndpointSummary("Depo oluştur")]
    [EndpointDescription("stok.manage. kind: normal | quarantine | damaged. Kod firmada benzersizdir (409 code_taken). Şube erişimi yoksa 403 branch_forbidden.")]
    public static async Task<Created<WarehouseDto>> CreateWarehouse(ISender sender, CreateWarehouseCommand command) {
        var warehouse = await sender.Send(command);
        return TypedResults.Created($"/api/erp/inventory/warehouses/{warehouse.Id}", warehouse);
    }

    [EndpointSummary("Depoyu güncelle")]
    [EndpointDescription("Varsayılan deponun türü Normal'den değiştirilemez (409 warehouse_is_default).")]
    public static async Task<Ok<WarehouseDto>> UpdateWarehouse(ISender sender, Guid id, UpdateWarehouseCommand command) {
        return TypedResults.Ok(await sender.Send(command with { Id = id }));
    }

    [EndpointSummary("Depoyu etkinleştir")]
    public static async Task<NoContent> ActivateWarehouse(ISender sender, Guid id) {
        await sender.Send(new SetWarehouseActiveCommand(id, true));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Depoyu pasifleştir")]
    [EndpointDescription("Varsayılan depo pasifleştirilemez (409 warehouse_is_default).")]
    public static async Task<NoContent> DeactivateWarehouse(ISender sender, Guid id) {
        await sender.Send(new SetWarehouseActiveCommand(id, false));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Depoyu sil")]
    [EndpointDescription("Varsayılan depo 409 warehouse_is_default, hareketi/sayımı olan depo 409 warehouse_in_use.")]
    public static async Task<NoContent> DeleteWarehouse(ISender sender, Guid id) {
        await sender.Send(new DeleteWarehouseCommand(id));
        return TypedResults.NoContent();
    }
}
