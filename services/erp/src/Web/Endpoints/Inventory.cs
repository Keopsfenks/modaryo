using ERP.Application.Common.Exceptions;
using ERP.Application.CurrentAccounts;
using ERP.Application.Inventory.Counts;
using ERP.Application.Inventory.Documents;
using ERP.Application.Inventory.Excel;
using ERP.Application.Inventory.Minimums;
using ERP.Application.Inventory.Queries;
using ERP.Application.Inventory.Transfers;
using ERP.Application.Inventory.Tracking.Queries;
using Microsoft.AspNetCore.Http.Features;
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
        groupBuilder.MapGet(ListBalances, "balances");
        groupBuilder.MapGet(GetItemCard, "item-card");
        groupBuilder.MapGet(GetValuation, "valuation");
        groupBuilder.MapPost(CreateDocument, "documents");
        groupBuilder.MapGet(ListDocuments, "documents");
        groupBuilder.MapGet(GetDocument, "documents/{id:guid}");
        groupBuilder.MapPost(CancelDocument, "documents/{id:guid}/cancel");
        groupBuilder.MapPost(CreateTransfer, "transfers");
        groupBuilder.MapGet(ListTransfers, "transfers");
        groupBuilder.MapGet(GetTransfer, "transfers/{id:guid}");
        groupBuilder.MapPost(CreateCount, "counts");
        groupBuilder.MapGet(ListCounts, "counts");
        groupBuilder.MapGet(GetCount, "counts/{id:guid}");
        groupBuilder.MapPut(SetCountLines, "counts/{id:guid}/lines");
        groupBuilder.MapPost(PostCount, "counts/{id:guid}/post");
        groupBuilder.MapPost(CancelCount, "counts/{id:guid}/cancel");
        groupBuilder.MapGet(ListMinimums, "minimums");
        groupBuilder.MapPut(SetMinimums, "minimums");
        groupBuilder.MapGet(BelowMinimum, "minimums/below");
        groupBuilder.MapGet(OpeningTemplate, "opening-import/template.xlsx");
        groupBuilder.MapPost(PreviewOpening, "opening-import/preview").DisableAntiforgery(); // CSRF koruması X-Modaryo-Csrf başlığıyla yapılır
        groupBuilder.MapPost(CommitOpening, "opening-import/{batchId:guid}/commit");
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

    [EndpointSummary("Stok durumu")]
    [EndpointDescription("stok.read. Depo x kalem satırları: physical, reserved, available (yalnızca Normal depoda), minQty, avgCost (yalnızca maliyet.read; yoksa null). Yalnızca erişilebilir şubelerin depoları. search kod/ad içinde arar; categoryId alt kategoriler dahil; onlyNonZero sıfır bakiyeleri gizler; belowMinimum minimumun altındakileri listeler. Yanıt { items, total }.")]
    public static async Task<Ok<PagedResult<BalanceRowDto>>> ListBalances(ISender sender, Guid? warehouseId, string? search, Guid? categoryId, bool onlyNonZero = false, bool belowMinimum = false, int page = 1, int pageSize = 50) {
        return TypedResults.Ok(await sender.Send(new ListBalancesQuery(warehouseId, search, categoryId, onlyNonZero, belowMinimum, page, pageSize)));
    }

    [EndpointSummary("Stok kartı")]
    [EndpointDescription("stok.read. from öncesi devir (openingQty), aralıktaki hareketler ve yürüyen bakiye; warehouseId yoksa erişilebilir depoların toplamı. Her hareket sourceType/sourceId taşır. unitCost/totalCost yalnızca maliyet.read ile gelir.")]
    public static async Task<Ok<ItemCardDto>> GetItemCard(ISender sender, Guid productId, Guid? variantId, Guid? warehouseId, DateOnly? from, DateOnly? to) {
        return TypedResults.Ok(await sender.Send(new ItemCardQuery(productId, variantId, warehouseId, from, to)));
    }

    [EndpointSummary("Stok değerleme")]
    [EndpointDescription("maliyet.read. date (varsayılan bugün) itibarıyla kalem x depo miktarı, birim maliyeti ve değeri; toplam değer.")]
    public static async Task<Ok<ValuationDto>> GetValuation(ISender sender, DateOnly? date, Guid? warehouseId) {
        return TypedResults.Ok(await sender.Send(new ValuationQuery(date, warehouseId)));
    }

    [EndpointSummary("Stok fişi oluştur")]
    [EndpointDescription("stok.manage. kind: receipt | issue | opening. Satır birimi ürünün temel birimi ya da tanımlı bir birimdir (400 invalid_unit); miktar ve birim maliyet satır birimindedir, temel birime çevrilir. Numara SF{yıl}{000001}. Yetersiz stok 409 insufficient_stock; belge ve hareketler tek işlemde yazılır.")]
    public static async Task<Created<DocumentCreated>> CreateDocument(ISender sender, CreateStockDocumentCommand command) {
        var created = await sender.Send(command);
        return TypedResults.Created($"/api/erp/inventory/documents/{created.Id}", created);
    }

    [EndpointSummary("Stok fişleri")]
    [EndpointDescription("stok.read. kind, warehouseId, from, to ile süzülür; yanıt { items, total }.")]
    public static async Task<Ok<PagedResult<StockDocumentRowDto>>> ListDocuments(ISender sender, string? kind, Guid? warehouseId, DateOnly? from, DateOnly? to, int page = 1, int pageSize = 50) {
        return TypedResults.Ok(await sender.Send(new ListStockDocumentsQuery(kind, warehouseId, from, to, page, pageSize)));
    }

    [EndpointSummary("Stok fişi")]
    [EndpointDescription("stok.read. Birim maliyet yalnızca maliyet.read ile gelir.")]
    public static async Task<Ok<StockDocumentDto>> GetDocument(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetStockDocumentQuery(id)));
    }

    public sealed record CancelBody(string Reason);

    [EndpointSummary("Stok fişini iptal et")]
    [EndpointDescription("stok.manage. Hareketlerin tersi bugünün tarihiyle yazılır; hiçbir şey silinmez. İkinci iptal 409 document_cancelled.")]
    public static async Task<NoContent> CancelDocument(ISender sender, Guid id, CancelBody body) {
        await sender.Send(new CancelStockDocumentCommand(id, body.Reason));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Depo transferi")]
    [EndpointDescription("stok.manage. Kaynak çıkışı ve hedef girişi birlikte yazılır; hedef giriş kaynağın birim maliyetini taşır. Hasarlı/karantina depoya geçiş de transferdir. Aynı depo 400 same_warehouse. Numara TF{yıl}{000001}.")]
    public static async Task<Created<DocumentCreated>> CreateTransfer(ISender sender, CreateTransferCommand command) {
        var created = await sender.Send(command);
        return TypedResults.Created($"/api/erp/inventory/transfers/{created.Id}", created);
    }

    [EndpointSummary("Transferler")]
    [EndpointDescription("stok.read. Yanıt { items, total }.")]
    public static async Task<Ok<PagedResult<TransferRowDto>>> ListTransfers(ISender sender, int page = 1, int pageSize = 50) {
        return TypedResults.Ok(await sender.Send(new ListTransfersQuery(page, pageSize)));
    }

    [EndpointSummary("Transfer")]
    public static async Task<Ok<TransferDto>> GetTransfer(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetTransferQuery(id)));
    }

    [EndpointSummary("Sayım başlat")]
    [EndpointDescription("stok.sayim. Sayım anındaki beklenen miktarlar satırlara yazılır (seri/lot takipli ürünler hariç). Depoda açık sayım varsa 409 count_open_exists. Numara SY{yıl}{000001}.")]
    public static async Task<Created<DocumentCreated>> CreateCount(ISender sender, CreateCountCommand command) {
        var created = await sender.Send(command);
        return TypedResults.Created($"/api/erp/inventory/counts/{created.Id}", created);
    }

    [EndpointSummary("Sayımlar")]
    public static async Task<Ok<IReadOnlyList<CountRowDto>>> ListCounts(ISender sender, Guid? warehouseId) {
        return TypedResults.Ok(await sender.Send(new ListCountsQuery(warehouseId)));
    }

    [EndpointSummary("Sayım")]
    public static async Task<Ok<CountDto>> GetCount(ISender sender, Guid id) {
        return TypedResults.Ok(await sender.Send(new GetCountQuery(id)));
    }

    public sealed record CountLinesBody(IReadOnlyList<CountLineInput> Lines);

    [EndpointSummary("Sayılan miktarları yaz")]
    [EndpointDescription("stok.sayim. countedQty null: sayılmadı. Kapanmış sayım 409 count_closed.")]
    public static async Task<NoContent> SetCountLines(ISender sender, Guid id, CountLinesBody body) {
        await sender.Send(new SetCountLinesCommand(id, body.Lines));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Sayımı kapat")]
    [EndpointDescription("stok.sayim. Fark = sayılan - sayım anındaki beklenen; CountAdjustment hareketleri yazılır. Kapanmış sayım 409 count_closed.")]
    public static async Task<NoContent> PostCount(ISender sender, Guid id) {
        await sender.Send(new PostCountCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Sayımı iptal et")]
    public static async Task<NoContent> CancelCount(ISender sender, Guid id) {
        await sender.Send(new CancelCountCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Minimum stok satırları")]
    public static async Task<Ok<IReadOnlyList<MinimumDto>>> ListMinimums(ISender sender, Guid warehouseId) {
        return TypedResults.Ok(await sender.Send(new ListMinimumsQuery(warehouseId)));
    }

    public sealed record MinimumsBody(Guid WarehouseId, IReadOnlyList<MinimumInput> Rows);

    [EndpointSummary("Minimum stok satırlarını değiştir")]
    [EndpointDescription("stok.manage. Deponun tüm satırlarını değiştirir; listede olmayanlar silinir.")]
    public static async Task<NoContent> SetMinimums(ISender sender, MinimumsBody body) {
        await sender.Send(new SetMinimumsCommand(body.WarehouseId, body.Rows));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Minimumun altındaki kalemler")]
    [EndpointDescription("stok.read. Kullanılabilir miktarı minimumun altında olan kalemler (Normal depolar).")]
    public static async Task<Ok<IReadOnlyList<BelowMinimumDto>>> BelowMinimum(ISender sender, Guid? warehouseId) {
        return TypedResults.Ok(await sender.Send(new BelowMinimumQuery(warehouseId)));
    }

    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const long MaxImportRequestBytes = OpeningSheet.MaxBytes + 1024 * 1024;

    [EndpointSummary("Açılış stoğu şablonu")]
    public static async Task<FileContentHttpResult> OpeningTemplate(ISender sender) {
        return TypedResults.File(await sender.Send(new OpeningTemplateQuery()), XlsxType, "acilis-stogu-sablonu.xlsx");
    }

    [EndpointSummary("Açılış stoğu içe aktarma önizlemesi")]
    [EndpointDescription("stok.manage. multipart/form-data: \"file\" (.xlsx, en fazla 5 MB, 5.000 satır). Sütunlar: Depo kodu, Ürün kodu, Varyant kodu, Miktar, Birim, Birim maliyet, Lot, SKT, Seri. Hiçbir şey yazılmaz; 30 dakika commit edilebilir. 400 invalid_file | invalid_template | too_many_rows | empty_file, 413 file_too_large.")]
    public static async Task<Ok<OpeningPreview>> PreviewOpening(HttpContext context, ISender sender) {
        var request = context.Request;
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = MaxImportRequestBytes;

        var tooLarge = new AppException(413, "file_too_large", "Dosya çok büyük; en fazla 5 MB yüklenebilir.");
        if (request.ContentLength > MaxImportRequestBytes) throw tooLarge;
        if (!request.HasFormContentType)
            throw new AppException(400, "file_required", "Dosya multipart/form-data olarak, \"file\" alanında gönderilmelidir.");

        IFormCollection form;
        try {
            form = await request.ReadFormAsync(context.RequestAborted);
        } catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge) {
            throw tooLarge;
        } catch (InvalidDataException) {
            throw new AppException(400, "file_required", "Dosya okunamadı.");
        }

        var file = form.Files.GetFile("file") ?? throw new AppException(400, "file_required", "\"file\" alanında bir dosya gönderilmelidir.");
        if (file.Length > OpeningSheet.MaxBytes) throw tooLarge;

        await using var stream = file.OpenReadStream();
        using var       buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, context.RequestAborted);
        return TypedResults.Ok(await sender.Send(new PreviewOpeningImportCommand(buffer.ToArray())));
    }

    [EndpointSummary("Açılış stoğu önizlemesini uygula")]
    [EndpointDescription("Hep ya da hiç: tek işlemde her satırı yeniden doğrular ve yazar (depo başına bir açılış fişi). Hatalı satır varsa 400 import_has_errors; önizlemeden sonra bir satırın sonucu değiştiyse 409 import_conflict (hiçbir şey yazılmaz, yanıttaki \"preview\" taze önizlemedir); eski önizleme 410 import_expired; ikinci uygulama 409 import_already_committed.")]
    public static async Task<IResult> CommitOpening(ISender sender, Guid batchId) {
        var outcome = await sender.Send(new CommitOpeningImportCommand(batchId));
        if (outcome.Done is { } done) return TypedResults.Ok(done);

        return TypedResults.Json(new {
                                     status  = 409,
                                     code    = "import_conflict",
                                     title   = "Önizlemeden sonra veriler değişti; hiçbir şey yazılmadı. Yeni önizlemeyi kontrol edin.",
                                     preview = outcome.Conflict
                                 }, statusCode: StatusCodes.Status409Conflict, contentType: "application/problem+json");
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
