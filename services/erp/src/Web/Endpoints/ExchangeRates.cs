using ERP.Application.Rates;
using ERP.Domain.Invoicing;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public class ExchangeRates : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/exchange-rates";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(ListRates, "");
        groupBuilder.MapGet(Lookup, "lookup");
        groupBuilder.MapPut(SetOverride, "override");
        groupBuilder.MapPost(Fetch, "fetch");
    }

    public sealed record OverrideInput(DateOnly Date, string Currency, RateType Type, decimal Rate);

    [EndpointSummary("Döviz kurları")]
    [EndpointDescription("fatura.read. date (varsayılan bugün) için USD/EUR/GBP kurları, tür başına. Tarihte kur yoksa en fazla 10 gün öncesine bakılır; effectiveDate gerçek tarihtir. overridden: firmanın elle kuru.")]
    public static async Task<Ok<IReadOnlyList<RateDto>>> ListRates(ISender sender, DateOnly? date) {
        return TypedResults.Ok(await sender.Send(new ListRatesQuery(date)));
    }

    [EndpointSummary("Tek kur")]
    [EndpointDescription("fatura.read. currency + date için kur; type verilmezse finans ayarındaki kur türü. TRY her zaman 1. 10 gün içinde kur yoksa 409 rate_missing.")]
    public static async Task<Ok<RateDto>> Lookup(ISender sender, string currency, DateOnly date, RateType? type) {
        return TypedResults.Ok(await sender.Send(new LookupRateQuery(currency, date, type)));
    }

    [EndpointSummary("Elle kur gir")]
    [EndpointDescription("kur.manage. Yalnızca bu firma için o gün/para birimi/tür kurunu TCMB kurunun yerine geçirir.")]
    public static async Task<Ok<RateDto>> SetOverride(ISender sender, OverrideInput input) {
        return TypedResults.Ok(await sender.Send(new SetRateOverrideCommand(input.Date, input.Currency, input.Type, input.Rate)));
    }

    [EndpointSummary("TCMB kurlarını şimdi çek")]
    [EndpointDescription("kur.manage. Bugünün ve son 10 günün eksik bültenlerini TCMB'den alır.")]
    public static async Task<Ok<FetchRatesResult>> Fetch(ISender sender) {
        return TypedResults.Ok(await sender.Send(new FetchRatesCommand()));
    }
}
