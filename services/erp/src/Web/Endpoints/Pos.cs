using ERP.Application.Catalog.Items;
using ERP.Application.Pos.Lookup;
using ERP.Application.Pos.Registers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Pos : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/pos";

    public sealed record RegisterBody(string Code, string Name, Guid BranchId, Guid WarehouseId, Guid CashAccountId, Guid CardAccountId, bool? IsActive, uint? Version);

    public sealed record ButtonsBody(IReadOnlyList<IReadOnlyList<QuickButtonInput?>>? Pages);

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(PosRegisters, "registers");
        groupBuilder.MapGet(PosGetRegister, "registers/{id:guid}");
        groupBuilder.MapPost(PosCreateRegister, "registers");
        groupBuilder.MapPut(PosUpdateRegister, "registers/{id:guid}");
        groupBuilder.MapPut(PosSetButtons, "registers/{id:guid}/buttons");
        groupBuilder.MapGet(PosLookup, "lookup");
        groupBuilder.MapGet(PosLookupSearch, "lookup/search");
    }

    [EndpointSummary("Kasa noktaları")]
    [EndpointDescription("pos.manage, pos.sell ya da pos.shift. Yalnızca oturumun erişebildiği şubelerin kasaları gelir. pos modülü kapalıysa 403 module_disabled.")]
    public static async Task<Ok<IReadOnlyList<RegisterRowDto>>> PosRegisters(ISender sender, bool? includeInactive) {
        return TypedResults.Ok(await sender.Send(new ListRegistersQuery(includeInactive ?? true)));
    }

    [EndpointSummary("Kasa noktası")]
    [EndpointDescription("pos.manage, pos.sell ya da pos.shift. pages: sayfa başına hücre dizisi (boş hücre null), hücre sırası satır-sütun sırasıdır (6 sütun).")]
    public static async Task<Ok<RegisterDto>> PosGetRegister(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetRegisterQuery(id)));
    }

    [EndpointSummary("Kasa noktası oluştur")]
    [EndpointDescription("pos.manage. Gövde: { code, name, branchId, warehouseId, cashAccountId, cardAccountId }. Depo şubenin aktif normal deposu, nakit hesabı şubenin TRY kasa hesabı, kart hesabı şubenin TRY kart POS hesabı olmalıdır (400 warehouse_invalid | cash_account_invalid | card_account_invalid). Kod firmada benzersizdir (409 code_taken); büyük harfe çevrilir. İlk kasada perakende müşteri carisi hazırlanır.")]
    public static async Task<Created<RegisterDto>> PosCreateRegister(ISender sender, RegisterBody body) {
        var dto = await sender.Send(new CreateRegisterCommand {
            Code = body.Code, Name = body.Name, BranchId = body.BranchId, WarehouseId = body.WarehouseId, CashAccountId = body.CashAccountId, CardAccountId = body.CardAccountId
        });
        return TypedResults.Created($"/api/erp/pos/registers/{dto.Id}", dto);
    }

    [EndpointSummary("Kasa noktası güncelle")]
    [EndpointDescription("pos.manage. Gövde oluşturmadakiyle aynı + { isActive?, version? }. Vardiyası olan kasanın kod, şube, depo ve hesapları değişmez (409 register_in_use). version verilirse uyuşmazlıkta 409 version_conflict.")]
    public static async Task<Ok<RegisterDto>> PosUpdateRegister(ISender sender, Guid id, RegisterBody body) {
        return TypedResults.Ok(await sender.Send(new UpdateRegisterCommand {
            Id = id, Code = body.Code, Name = body.Name, BranchId = body.BranchId, WarehouseId = body.WarehouseId, CashAccountId = body.CashAccountId,
            CardAccountId = body.CardAccountId, IsActive = body.IsActive ?? true, Version = body.Version ?? 0
        }));
    }

    [EndpointSummary("Hızlı tuş yerleşimi")]
    [EndpointDescription("pos.manage. Gövde: { pages: [[{ productId, variantId?, presetQty?, color }|null, ...]] }. En çok 10 sayfa, sayfada en çok 36 hücre (6x6); hücre sırası satır-sütun sırasıdır. Yerleşim bütünüyle değiştirilir. Renk #RRGGBB (boşsa varsayılan).")]
    public static async Task<Ok<RegisterDto>> PosSetButtons(ISender sender, Guid id, ButtonsBody body) {
        return TypedResults.Ok(await sender.Send(new SetButtonsCommand(id, body.Pages)));
    }

    [EndpointSummary("Kasa ürün sorgusu")]
    [EndpointDescription("pos.sell. code: terazi barkodu, katalog barkodu ya da kalem/varyant kodu (bu sırayla). Biçimi terazi barkoduna uyan ama kontrol hanesi hatalı kod 400 scale_barcode_invalid, bulunamayan 404. Yanıt: kalem, birim ve çevrim, miktar (terazide ağırlık ya da tutardan türetilir), perakende müşteri için satış fiyatı, tutar tipi terazi barkodunda fixedTotal, kampanya ve diğer şubelerin varsayılan deposundaki kullanılabilir miktarlar (yalnızca miktar; registerId verilirse o kasanın şubesi hariç tutulur).")]
    public static async Task<Ok<PosLookupDto>> PosLookup(ISender sender, string code, Guid? registerId) {
        return TypedResults.Ok(await sender.Send(new PosLookupQuery(code, registerId)));
    }

    [EndpointSummary("Kasa kalem arama")]
    [EndpointDescription("pos.sell. q: kod, ad, barkod ya da tedarikçi kodu; en çok 20 aktif kalem. Varyantlı kart varyant başına bir kalem verir.")]
    public static async Task<Ok<IReadOnlyList<SellableItem>>> PosLookupSearch(ISender sender, string? q) {
        return TypedResults.Ok(await sender.Send(new PosSearchQuery(q)));
    }
}
