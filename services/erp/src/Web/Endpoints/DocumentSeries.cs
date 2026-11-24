using ERP.Application.Invoicing.Series;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class DocumentSeries : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/document-series";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(ListSeries, "");
        groupBuilder.MapPost(CreateSeries, "");
        groupBuilder.MapPut(UpdateSeries, "{id:guid}");
    }

    [EndpointSummary("Belge serileri")]
    [EndpointDescription("fatura.read. branchId ile süzülür; yalnızca erişilebilir şubelerin serileri. nextPreview sıradaki numarayı gösterir.")]
    public static async Task<Ok<IReadOnlyList<SeriesDto>>> ListSeries(ISender sender, Guid? branchId) {
        return TypedResults.Ok(await sender.Send(new ListSeriesQuery(branchId)));
    }

    [EndpointSummary("Seri oluştur")]
    [EndpointDescription("ayarlar.belgeler. kindGroup: sales | purchase. e-Belge (EInvoice/EArchive) önekı tam 3 karakter [A-Z0-9], kâğıt öneki 1-10 karakter. (grup, şube, mod) için ilk seri otomatik varsayılan olur; isDefault true ise öncekinin varsayılanlığı kalkar. Aynı önek/yıl/mod/grup 409 series_exists.")]
    public static async Task<Created<SeriesDto>> CreateSeries(ISender sender, CreateSeriesCommand command) {
        var series = await sender.Send(command);
        return TypedResults.Created($"/api/erp/document-series/{series.Id}", series);
    }

    [EndpointSummary("Seriyi güncelle")]
    [EndpointDescription("ayarlar.belgeler. Numara alınmış serinin öneki/yılı/modu değiştirilemez (409 series_in_use). Varsayılan seri PUT ile kaldırılamaz; başka seri varsayılan yapılınca düşer.")]
    public static async Task<Ok<SeriesDto>> UpdateSeries(ISender sender, Guid id, UpdateSeriesCommand command) {
        return TypedResults.Ok(await sender.Send(command with { Id = id }));
    }
}
