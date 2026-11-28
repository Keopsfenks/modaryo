using ERP.Application.Catalog.Items;
using ERP.Application.Pos.Lookup;
using ERP.Application.Pos.Registers;
using ERP.Application.Pos.Reports;
using ERP.Application.Pos.Returns;
using ERP.Application.Pos.Shifts;
using ERP.Domain.Pos;
using ERP.Application.Pos.Sales;
using ERP.Application.Pos.Sales.Commands;
using ERP.Application.Pos.Sales.Queries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class Pos : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/pos";

    public sealed record RegisterBody(string Code, string Name, Guid BranchId, Guid WarehouseId, Guid CashAccountId, Guid CardAccountId, bool? IsActive, uint? Version);

    public sealed record OpenShiftBody(Guid RegisterId, decimal OpeningCash);

    public sealed record CloseShiftBody(decimal CountedCash, string? Note);

    public sealed record CashMoveBody(CashMoveDirection Kind, decimal Amount, string Reason);

    public sealed record CancelBody(string Reason);

    public sealed record ButtonsBody(IReadOnlyList<IReadOnlyList<QuickButtonInput?>>? Pages);

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(PosRegisters, "registers");
        groupBuilder.MapGet(PosGetRegister, "registers/{id:guid}");
        groupBuilder.MapPost(PosCreateRegister, "registers");
        groupBuilder.MapPut(PosUpdateRegister, "registers/{id:guid}");
        groupBuilder.MapPut(PosSetButtons, "registers/{id:guid}/buttons");
        groupBuilder.MapGet(PosLookup, "lookup");
        groupBuilder.MapGet(PosLookupSearch, "lookup/search");
        groupBuilder.MapPost(PosCreateSale, "sales");
        groupBuilder.MapGet(PosGetSale, "sales/{id:guid}");
        groupBuilder.MapGet(PosGetSaleByNumber, "sales/by-number/{number}");
        groupBuilder.MapPost(PosCancelSale, "sales/{id:guid}/cancel");
        groupBuilder.MapPost(PosOpenShift, "shifts/open");
        groupBuilder.MapPost(PosCloseShift, "shifts/{id:guid}/close");
        groupBuilder.MapPost(PosCashMove, "shifts/{id:guid}/cash-moves");
        groupBuilder.MapGet(PosShifts, "shifts");
        groupBuilder.MapGet(PosCurrentShift, "shifts/current");
        groupBuilder.MapPost(PosCreateReturn, "returns");
        groupBuilder.MapGet(PosShiftReport, "reports/shift/{id:guid}");
        groupBuilder.MapGet(PosDayReport, "reports/day");
        groupBuilder.MapGet(PosCashierReport, "reports/cashiers");
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
    [EndpointSummary("Satış kaydet")]
    [EndpointDescription("pos.sell. Gövde: { clientSaleId, registerId, customerId?, lines: [{ productId, variantId?, unit?, quantity, unitPrice?, manualDiscountPercent?, fixedTotal? }], payments: [{ method: cash|card|onAccount, amount }] }. clientSaleId istemcinin ürettiği tek seferlik kimliktir (zorunlu): aynı kimlikle tekrar gelen gönderim yeni satış yazmaz, kayıtlı satışı 200 ve replayed=true ile döndürür. Fiyatlar sunucuda çözülür (P2 önceliği); kampanya otomatik uygulanır; elle iskonto iskonto politikasına tabidir (403 discount_limit_exceeded). Kasiyerin bu kasada açık vardiyası olmalıdır (409 shift_required). Ödemeler toplamı karşılamalıdır (400 payment_insufficient); kart kalanı aşamaz (400 card_exceeds_remaining); nakit fazlası para üstüdür. Veresiye (method onAccount) perakende dışı müşteri ister (400 veresiye_requires_customer); kredi limiti aşılırsa satis.limit-asimi gerekir (409 limit_approval_required). Yanıt: { id, number, total, change, invoiceNumber?, replayed, slip }.")]
    public static async Task<Results<Created<SaleResultDto>, Ok<SaleResultDto>>> PosCreateSale(ISender sender, SaleInput body) {
        var result = await sender.Send(new CreateSaleCommand(body));
        return result.Replayed ? TypedResults.Ok(result) : TypedResults.Created($"/api/erp/pos/sales/{result.Id}", result);
    }

    [EndpointSummary("Satış")]
    [EndpointDescription("pos.sell, pos.return, pos.reports ya da pos.manage. Satır, ödeme ve fiş modeli (slip).")]
    public static async Task<Ok<SaleDto>> PosGetSale(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetSaleQuery(id)));
    }

    [EndpointSummary("Fiş numarasıyla satış")]
    [EndpointDescription("pos.sell, pos.return, pos.reports ya da pos.manage. Fiş numarası ya da fiş barkodu (numaranın kendisi).")]
    public static async Task<Ok<SaleDto>> PosGetSaleByNumber(ISender sender, string number) {
        return TypedResults.Ok(await sender.Send(new GetSaleByNumberQuery(number)));
    }

    [EndpointSummary("Satışı iptal et")]
    [EndpointDescription("pos.return. Gövde: { reason }. Yalnızca aynı gün ve hâlâ açık vardiyadaki satış iptal edilir (409 cancel_not_allowed; iade kullanın); iadesi olan satış iptal edilemez (409 sale_has_returns). Stok bir kez geri alınır, hesap hareketleri ve (e-Arşiv modunda) fatura ile tahsilatlar iptal edilir.")]
    public static async Task<Ok<SaleDto>> PosCancelSale(ISender sender, Guid id, CancelBody body) {
        return TypedResults.Ok(await sender.Send(new CancelSaleCommand(id, body.Reason ?? string.Empty)));
    }

    [EndpointSummary("Vardiya aç")]
    [EndpointDescription("pos.shift. Gövde: { registerId, openingCash }. Kasada aynı anda tek açık vardiya olur (409 shift_already_open).")]
    public static async Task<Created<ShiftDto>> PosOpenShift(ISender sender, OpenShiftBody body) {
        var dto = await sender.Send(new OpenShiftCommand(body.RegisterId, body.OpeningCash));
        return TypedResults.Created($"/api/erp/pos/shifts/{dto.Id}", dto);
    }

    [EndpointSummary("Vardiya kapat")]
    [EndpointDescription("pos.shift (vardiyanın sahibi ya da pos.manage). Gövde: { countedCash, note? }. Beklenen nakit = açılış + net nakit satış - nakit iade + kasa girişi - kasa çıkışı; fark (sayılan - beklenen) saklanır. Yanıt kasa raporudur.")]
    public static async Task<Ok<ShiftReportDto>> PosCloseShift(ISender sender, Guid id, CloseShiftBody body) {
        return TypedResults.Ok(await sender.Send(new CloseShiftCommand(id, body.CountedCash, body.Note)));
    }

    [EndpointSummary("Kasa girişi/çıkışı")]
    [EndpointDescription("pos.cashmove. Gövde: { kind: in|out, amount, reason }. Açık vardiyada beklenen nakdi etkiler; eksiye düşerse ayara göre uyarı (cash_negative) ya da 409.")]
    public static async Task<Ok<CashMoveResultDto>> PosCashMove(ISender sender, Guid id, CashMoveBody body) {
        return TypedResults.Ok(await sender.Send(new AddCashMoveCommand(id, body.Kind, body.Amount, body.Reason)));
    }

    [EndpointSummary("Vardiyalar")]
    [EndpointDescription("pos.shift, pos.manage ya da pos.reports. Yönetici/rapor yetkisi olmayan kasiyer yalnızca kendi vardiyalarını görür.")]
    public static async Task<Ok<IReadOnlyList<ShiftDto>>> PosShifts(ISender sender, Guid? registerId, ShiftStatus? status, int? limit) {
        return TypedResults.Ok(await sender.Send(new ListShiftsQuery(registerId, status, limit ?? 50)));
    }

    [EndpointSummary("Açık vardiyam")]
    [EndpointDescription("Kasiyerin bu kasadaki açık vardiyası; yoksa 404.")]
    public static async Task<Ok<ShiftDto>> PosCurrentShift(ISender sender, Guid registerId) {
        return TypedResults.Ok(await sender.Send(new CurrentShiftQuery(registerId)));
    }

    [EndpointSummary("İade / değişim")]
    [EndpointDescription("pos.return. Gövde: { clientReturnId?, saleNumber, registerId?, lines: [{ saleLineId, quantity }], refund: [{ method: cash|card, amount }], exchangeLines?: [satış satırları], exchangePayments?: [{ method, amount }], reason? }. Miktar satılan - iade edilenden fazla olamaz (400 return_exceeds_sold). İade dağılımı (iade değeri - veresiye payı - değişim mahsubu) toplamı tutmalıdır (400 refund_mismatch). Değişimde fark müşteriden alınır (exchangePayments) ya da iade edilir (refund).")]
    public static async Task<Ok<ReturnResultDto>> PosCreateReturn(ISender sender, ReturnInput body) {
        return TypedResults.Ok(await sender.Send(new CreateReturnCommand(body)));
    }

    [EndpointSummary("Vardiya raporu (kasa raporu)")]
    [EndpointDescription("pos.shift (kendi vardiyası), pos.manage ya da pos.reports.")]
    public static async Task<Ok<ShiftReportDto>> PosShiftReport(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new ShiftReportQuery(id)));
    }

    [EndpointSummary("Gün sonu raporu")]
    [EndpointDescription("pos.reports. Kasa ve gün başına: ödeme yöntemi toplamları, KDV özeti, iadeler, kampanya indirimi, kasiyer başına satış, ilk ve son fiş numarası.")]
    public static async Task<Ok<DayReportDto>> PosDayReport(ISender sender, Guid registerId, DateOnly date) {
        return TypedResults.Ok(await sender.Send(new DayReportQuery(registerId, date)));
    }

    [EndpointSummary("Kasiyer raporu")]
    [EndpointDescription("pos.reports. from/to (en çok 1 yıl), registerId isteğe bağlı: kasiyer başına satış, iptal ve iade toplamları.")]
    public static async Task<Ok<IReadOnlyList<CashierSalesDto>>> PosCashierReport(ISender sender, DateOnly from, DateOnly to, Guid? registerId) {
        return TypedResults.Ok(await sender.Send(new CashierReportQuery(from, to, registerId)));
    }
}
